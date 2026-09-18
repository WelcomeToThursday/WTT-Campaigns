using System.Collections.Concurrent;
using SPTarkov.DI.Annotations;
using WTT.Campaigns.Server.Profiles;
using WTT.Campaigns.Server.Seasons;
using WTT.Campaigns.Shared.Authoring;
using WTT.Campaigns.Shared.Missions;
using WTT.Campaigns.Shared.Seasons;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Server.Editor;

/// <summary>
/// Owns the editor's short lived, draft backed mission rehearsal.  It uses the
/// editor session profile and never writes the draft or the selected campaign
/// character.  The normal campaign mission service remains the authority for
/// real character raids.
/// </summary>
[Injectable(InjectionType.Singleton)]
public sealed class EditorMissionTestService(SeasonRepository repository, SeasonService seasons)
{
    private static readonly TimeSpan RunLifetime = TimeSpan.FromMinutes(20);

    private sealed class RunState
    {
        internal required string SessionId { get; init; }
        internal required string ProfileId { get; init; }
        internal required string DraftId { get; init; }
        internal required string LayoutId { get; init; }
        internal required MissionDefinition Mission { get; init; }
        internal required MapLayout Layout { get; init; }
        internal required MissionRun Run { get; set; }
        internal MissionCheckpoint? Checkpoint;
        internal required long DraftRevision { get; init; }
        internal required string ContentHash { get; init; }
        internal object Gate { get; } = new();
        internal Dictionary<string, (string Value, long Generation)> ProgressOperations { get; } = new(StringComparer.Ordinal);
        internal bool UseEncounters;
        internal bool LayoutCheckpointTest;
        internal DateTimeOffset LastTouched = DateTimeOffset.UtcNow;
        internal bool Completed;
    }

    private readonly ConcurrentDictionary<string, RunState> _runs = new(StringComparer.Ordinal);

    public EditorTestMissionResponse Handle(string transportIdentity, EditorTestMissionRequest request)
    {
        PruneExpiredRuns(DateTimeOffset.UtcNow);
        if (request.Version is not (1 or 2))
            throw new InvalidOperationException("Update both editor components together.");

        var session = RequireSession(transportIdentity, request, "running a mission test");
        return request.Action switch
        {
            EditorTestActions.Prepare => Prepare(session, request),
            EditorTestActions.PrepareCheckpoints => Prepare(session, request),
            EditorTestActions.Progress => Progress(session, request),
            EditorTestActions.End => End(session, request),
            EditorTestActions.Reset => Reset(session, request),
            _ => throw new InvalidOperationException("Unknown editor mission test action."),
        };
    }

    private EditorSessionRegistry.Session RequireSession(string transportIdentity, EditorTestMissionRequest request, string operation)
    {
        var session = EditorSessionRegistry.Resolve(transportIdentity, request.SessionId, DateTimeOffset.UtcNow).RequireMap(operation);
        using var lease = seasons.Enter(session.Owner);
        // The lease is deliberately held only while validating and touching the
        // editor session.  The route state itself is independent disposable data.
        if (!string.IsNullOrWhiteSpace(request.DraftId) && request.DraftId != session.Draft)
            throw new InvalidOperationException("The test request belongs to another draft.");
        var checkpointTest = request.Action == EditorTestActions.PrepareCheckpoints
            || (_runs.TryGetValue(session.Id, out var rehearsal) && rehearsal.LayoutCheckpointTest
                && rehearsal.Run.RunId == request.RunId && rehearsal.LayoutId == request.LayoutId);
        if (checkpointTest)
        {
            if (!repository.Load(session.Draft).Definition.MapLayouts.Any(l => l.Id == request.LayoutId && l.Location == session.Location))
                throw new InvalidOperationException("The checkpoint layout does not belong to this draft and open map.");
        }
        else if (!string.IsNullOrWhiteSpace(request.LayoutId) && request.LayoutId != session.Layout)
            throw new InvalidOperationException("The test request belongs to another layout.");
        if (string.IsNullOrWhiteSpace(session.Draft) || (!checkpointTest && string.IsNullOrWhiteSpace(session.Layout)))
            throw new InvalidOperationException("Select a saved draft and mission layout before testing it.");
        session.Contact = DateTimeOffset.UtcNow;
        return session;
    }

    private EditorTestMissionResponse Prepare(EditorSessionRegistry.Session session, EditorTestMissionRequest request)
    {
        var draft = repository.Load(session.Draft);
        if (draft.Status != DraftStatus.Active)
            throw new InvalidOperationException("Restore this draft before testing it.");

        var checkpointTest = request.Action == EditorTestActions.PrepareCheckpoints;
        var mission = checkpointTest
            ? EditorCheckpointTest.Definition(draft.Definition.MapLayouts.SingleOrDefault(l => l.Id == request.LayoutId)
                ?? throw new InvalidOperationException("The selected layout is unavailable in this draft."))
            : SelectMission(draft.Definition, session.Layout, request.MissionId);
        if (request.Version < 2 && MissionLogic.HasLogic(mission))
            throw new InvalidOperationException("Update both editor components for mission events and objectives.");
        if (!checkpointTest && !string.Equals(mission.LayoutId, session.Layout, StringComparison.Ordinal))
            throw new InvalidOperationException("Select the mission's linked layout before testing it.");
        var layout =
            draft.Definition.MapLayouts.SingleOrDefault(l => l.Id == mission.LayoutId)
            ?? throw new InvalidOperationException("The mission layout is unavailable in this draft.");
        if (!string.Equals(layout.Location, session.Location, StringComparison.Ordinal))
            throw new InvalidOperationException("Open the mission layout's map before testing it.");
        var errors = MapLayoutRules.Errors(layout, walkthrough: true);
        errors.AddRange(MissionLogicRules.Errors(mission, layout));
        if (errors.Count > 0)
            throw new InvalidOperationException(errors[0]);

        var hash = SeasonRepository.GameplayHash(draft.Definition);
        var run = new MissionRun
        {
            RunId = SeasonRepository.NewId(),
            CharacterId = session.Profile,
            RaidId = "editor:" + session.Id,
            MissionId = mission.Id,
            LayoutId = layout.Id,
            ContentRevision = draft.Revision,
            ContentHash = hash,
            EncounterToken = SeasonRepository.NewId(),
            Status = MissionRunStatuses.Active,
            StartedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };
        var state = new RunState
        {
            SessionId = session.Id,
            ProfileId = session.Profile,
            DraftId = session.Draft,
            LayoutId = layout.Id,
            Mission = SeasonCompiler.Copy(mission),
            Layout = SeasonCompiler.Copy(layout),
            Run = run,
            DraftRevision = draft.Revision,
            ContentHash = hash,
            UseEncounters = request.UseEncounters,
            LayoutCheckpointTest = checkpointTest,
        };
        var replayed = _runs.TryGetValue(session.Id, out var previous) && previous.Completed;
        _runs[session.Id] = state;
        return Response(state, "Mission test ready. Reach every checkpoint, then the authored exit.", replayed);
    }

    private EditorTestMissionResponse Progress(EditorSessionRegistry.Session session, EditorTestMissionRequest request)
    {
        var state = RequireRun(session, request);
        lock (state.Gate)
        {
            EnsureCurrentContent(state);
            if (state.Run.Status != MissionRunStatuses.Active)
                throw new InvalidOperationException("This mission test has already ended (" + state.Run.Status
                    + "). Start a new test; an ended test cannot accept checkpoint progress or retries.");
            if (string.IsNullOrWhiteSpace(request.Kind))
                throw new InvalidOperationException("A mission progress kind is required.");
            if (string.IsNullOrWhiteSpace(request.OperationId))
                throw new InvalidOperationException("A stable mission progress operation identity is required.");

            var operationValue = Newtonsoft.Json.JsonConvert.SerializeObject(new
            { request.Kind, request.CheckpointId, request.AttemptGeneration, request.Signals, request.Actors });
            if (state.ProgressOperations.TryGetValue(request.OperationId, out var previousValue))
            {
                if (previousValue.Generation != state.Run.AttemptGeneration || !string.Equals(previousValue.Value, operationValue, StringComparison.Ordinal))
                    throw new InvalidOperationException("The mission progress operation identity was reused for another transition.");
                return Response(state, "Mission progress already recorded.", state.Completed, committed: true);
            }

            RequireAttempt(state.Run, request);
            if (request.Kind is "start-checkpoint" or "retry-prepare" or "retry-commit" or "defeat")
            {
                if (!state.Mission.CheckpointRetries) throw new InvalidOperationException("Checkpoint retries are disabled.");
                switch (request.Kind)
                {
                    case "start-checkpoint":
                        if (state.Checkpoint != null || state.Run.Logic.Started || state.Run.NextCheckpointIndex != 0)
                            throw new InvalidOperationException("The mission start checkpoint is already sealed.");
                        state.Checkpoint = new MissionCheckpoint(state.Run, "");
                        break;
                    case "defeat":
                        if (state.Run.ExitReached) throw new InvalidOperationException("The mission has already exited.");
                        state.Run.PlayerDefeated = true;
                        break;
                    case "retry-prepare":
                        if (!state.Run.PlayerDefeated && state.Run.Logic.Failure.Length == 0)
                            throw new InvalidOperationException("This mission attempt has not failed.");
                        state.Run = (state.Checkpoint ?? throw new InvalidOperationException("No raid checkpoint is available.")).BeginRestore(state.Run);
                        state.Run.RestoredActorIds = state.Run.Logic.Actors.Values.Where(a => a.Spawned && !a.Dead)
                            .ToDictionary(a => a.ProfileId, _ => SeasonRepository.NewId());
                        break;
                    case "retry-commit":
                        (state.Checkpoint ?? throw new InvalidOperationException("No raid checkpoint is available."))
                            .CommitRestore(state.Mission, state.Layout, state.Run, state.Run.RestoredActorIds);
                        break;
                }
            }
            else if (request.Kind.Equals("Observations", StringComparison.OrdinalIgnoreCase))
            {
                var candidate = SeasonCompiler.Copy(state.Run);
                foreach (var actor in request.Actors)
                {
                    if (candidate.Logic.Actors.ContainsKey(actor.ProfileId)) continue;
                    var encounter = state.Layout.Encounters.FirstOrDefault(e => e.Id == actor.EncounterId);
                    var wave = encounter?.Waves.FirstOrDefault(w => w.Id == actor.WaveId);
                    var roster = wave?.Roster.FirstOrDefault(r => r.Id == actor.RosterId && r.SquadId == actor.SquadId);
                    if (roster == null || string.IsNullOrWhiteSpace(actor.ProfileId) || actor.Spawned || actor.Dead
                        || candidate.Logic.Actors.Values.Count(a => a.RosterId == roster.Id) >= roster.Count)
                        throw new InvalidOperationException("Invalid rehearsal actor registration.");
                    candidate.Logic.Actors.Add(actor.ProfileId, SeasonCompiler.Copy(actor));
                }
                MissionObservationRules.Apply(state.Mission, state.Layout, candidate, new MissionRequest
                {
                    AttemptGeneration = request.AttemptGeneration, Signals = request.Signals,
                }, DateTimeOffset.UtcNow.ToUnixTimeSeconds() - state.Run.StartedAt);
                state.Run.Logic = candidate.Logic;
            }
            else if (request.Kind.Equals("Checkpoint", StringComparison.OrdinalIgnoreCase))
            {
                if (!MissionLogic.CanAdvance(state.Mission, state.Run.Logic, request.CheckpointId, out var objectiveError))
                    throw new InvalidOperationException(objectiveError);
                if (!MissionRunRules.TryCheckpoint(state.Run, state.Layout.Checkpoints, request.CheckpointId, out var error))
                    throw new InvalidOperationException(error);
                if (state.Mission.CheckpointRetries) state.Checkpoint = new MissionCheckpoint(state.Run, request.CheckpointId);
                MissionLogic.Apply(state.Mission, state.Layout, state.Run.Logic, new() { Kind = MissionSignals.Checkpoint, TargetId = request.CheckpointId, Time = state.Run.Logic.Time });
            }
            else if (request.Kind.Equals("Exit", StringComparison.OrdinalIgnoreCase))
            {
                if (!MissionLogic.CanAdvance(state.Mission, state.Run.Logic, "", out var objectiveError))
                    throw new InvalidOperationException(objectiveError);
                if (!MissionRunRules.TryExit(state.Run, state.Layout.Checkpoints, state.Layout.Exit, request.CheckpointId, out var error))
                    throw new InvalidOperationException(error);
                MissionLogic.Apply(state.Mission, state.Layout, state.Run.Logic, new() { Kind = MissionSignals.Exit, Time = state.Run.Logic.Time });
            }
            else
            {
                throw new InvalidOperationException("Unsupported mission test progress kind.");
            }

            state.ProgressOperations[request.OperationId] = (operationValue, state.Run.AttemptGeneration);

            return Response(
                state,
                request.Kind.Equals("Exit", StringComparison.OrdinalIgnoreCase)
                    ? "Authored exit reached. Finalizing the mission test."
                    : "Checkpoint secured.",
                state.Completed,
                committed: true
            );
        }
    }

    private EditorTestMissionResponse End(EditorSessionRegistry.Session session, EditorTestMissionRequest request)
    {
        var state = RequireRun(session, request);
        lock (state.Gate)
        {
            EnsureCurrentContent(state);
            RequireAttempt(state.Run, request);
            if (state.Run.Status == MissionRunStatuses.Succeeded)
                return Response(state, "Mission test complete.", state.Completed, committed: true);
            if (state.Run.Status != MissionRunStatuses.Active)
                return Response(state, "Mission test failed.", state.Completed);
            if (!MissionLogic.CanAdvance(state.Mission, state.Run.Logic, "", out var objectiveError))
                throw new InvalidOperationException(objectiveError);
            if (
                !MissionRunRules.IsSuccessfulExtraction(
                    state.Run,
                    state.Layout.Checkpoints,
                    state.Layout.Exit,
                    "Survived",
                    state.Layout.Exit?.Name,
                    out var error
                )
            )
            {
                state.Run.Status = MissionRunStatuses.Failed;
                state.Run.FailureReason = error;
                state.Run.FinishedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                throw new InvalidOperationException(error);
            }

            state.Run.Status = MissionRunStatuses.Succeeded;
            state.Run.FinishedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            state.Completed = true;
            return Response(state, "Mission test complete. Press Retry or return to editing.", replayed: false, committed: true);
        }
    }

    private EditorTestMissionResponse Reset(EditorSessionRegistry.Session session, EditorTestMissionRequest request)
    {
        if (
            !string.IsNullOrWhiteSpace(request.RunId)
            && _runs.TryGetValue(session.Id, out var current)
            && current.Run.RunId != request.RunId
        )
            throw new InvalidOperationException("The mission test run has already been replaced.");
        _runs.TryRemove(session.Id, out _);
        return new EditorTestMissionResponse
        {
            SessionId = session.Id,
            ProfileId = session.Profile,
            DraftId = session.Draft,
            LayoutId = session.Layout,
            Status = "Reset",
            Message = "Mission test reset.",
        };
    }

    private RunState RequireRun(EditorSessionRegistry.Session session, EditorTestMissionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RunId))
            throw new InvalidOperationException("A mission test run is required.");
        if (!_runs.TryGetValue(session.Id, out var state) || state.Run.RunId != request.RunId)
            throw new InvalidOperationException("The mission test run is unavailable or stale.");
        if (state.ProfileId != session.Profile || state.DraftId != session.Draft
            || (state.LayoutCheckpointTest ? state.LayoutId != request.LayoutId || state.Layout.Location != session.Location : state.LayoutId != session.Layout))
            throw new InvalidOperationException("The editor mission test session changed.");
        state.LastTouched = DateTimeOffset.UtcNow;
        return state;
    }

    private static void RequireAttempt(MissionRun run, EditorTestMissionRequest request)
    {
        if ((run.Restoring && request.Kind != "retry-commit") || run.AttemptGeneration != request.AttemptGeneration)
            throw new InvalidOperationException("The mission test attempt is restoring or has been retired.");
    }

    private void EnsureCurrentContent(RunState state)
    {
        var draft = repository.Load(state.DraftId);
        if (
            draft.Status != DraftStatus.Active
            || draft.Revision != state.DraftRevision
            || !string.Equals(SeasonRepository.GameplayHash(draft.Definition), state.ContentHash, StringComparison.Ordinal)
        )
            throw new InvalidOperationException("The draft changed during the mission test. Reset and prepare it again.");
    }

    private EditorTestMissionResponse Response(RunState state, string message, bool replayed = false, bool committed = false)
    {
        var descriptor = new MissionDescriptor
        {
            Definition = SeasonCompiler.Copy(state.Mission),
            Layout = SeasonCompiler.Copy(state.Layout),
            CharacterId = state.ProfileId,
            SessionId = state.SessionId,
            RunId = state.Run.RunId,
            RaidId = state.Run.RaidId,
            ContentRevision = state.Run.ContentRevision,
            ContentHash = state.Run.ContentHash,
            EncounterToken = state.Run.EncounterToken,
            MissionOnlyExtracts = true,
        };
        return new EditorTestMissionResponse
        {
            SessionId = state.SessionId,
            ProfileId = state.ProfileId,
            DraftId = state.DraftId,
            LayoutId = state.LayoutId,
            MissionId = state.Mission.Id,
            RunId = state.Run.RunId,
            ContentRevision = state.Run.ContentRevision,
            ContentHash = state.Run.ContentHash,
            Status = state.Run.Status,
            Disposable = true,
            SourcePreserved = true,
            Replayed = replayed,
            Committed = committed,
            UseEncounters = state.UseEncounters,
            Descriptor = descriptor,
            Run = SeasonCompiler.Copy(state.Run),
            Message = message,
        };
    }

    private static MissionDefinition SelectMission(SeasonDefinition definition, string layoutId, string requestedId)
    {
        var mission = string.IsNullOrWhiteSpace(requestedId)
            ? definition.Missions.SingleOrDefault(m => m.LayoutId == layoutId)
            : definition.Missions.SingleOrDefault(m => m.Id == requestedId);
        return mission
            ?? throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(requestedId)
                    ? "The selected layout is not linked to a mission in this draft."
                    : "The selected mission is unavailable in this draft."
            );
    }

    private void PruneExpiredRuns(DateTimeOffset now)
    {
        foreach (var entry in _runs)
            if (now - entry.Value.LastTouched > RunLifetime)
                _runs.TryRemove(entry.Key, out _);
    }
}
