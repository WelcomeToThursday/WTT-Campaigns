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
        internal required MissionRun Run { get; init; }
        internal required long DraftRevision { get; init; }
        internal required string ContentHash { get; init; }
        internal object Gate { get; } = new();
        internal Dictionary<string, string> ProgressOperations { get; } = new(StringComparer.Ordinal);
        internal bool UseEncounters;
        internal DateTimeOffset LastTouched = DateTimeOffset.UtcNow;
        internal bool Completed;
    }

    private readonly ConcurrentDictionary<string, RunState> _runs = new(StringComparer.Ordinal);

    public EditorTestMissionResponse Handle(string transportIdentity, EditorTestMissionRequest request)
    {
        PruneExpiredRuns(DateTimeOffset.UtcNow);
        if (request.Version != 1)
            throw new InvalidOperationException("Update both editor components together.");

        var session = RequireSession(transportIdentity, request, "running a mission test");
        return request.Action switch
        {
            EditorTestActions.Prepare => Prepare(session, request),
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
        if (!string.IsNullOrWhiteSpace(request.LayoutId) && request.LayoutId != session.Layout)
            throw new InvalidOperationException("The test request belongs to another layout.");
        if (string.IsNullOrWhiteSpace(session.Draft) || string.IsNullOrWhiteSpace(session.Layout))
            throw new InvalidOperationException("Select a saved draft and mission layout before testing it.");
        session.Contact = DateTimeOffset.UtcNow;
        return session;
    }

    private EditorTestMissionResponse Prepare(EditorSessionRegistry.Session session, EditorTestMissionRequest request)
    {
        var draft = repository.Load(session.Draft);
        if (draft.Status != DraftStatus.Active)
            throw new InvalidOperationException("Restore this draft before testing it.");

        var mission = SelectMission(draft.Definition, session.Layout, request.MissionId);
        if (!string.Equals(mission.LayoutId, session.Layout, StringComparison.Ordinal))
            throw new InvalidOperationException("Select the mission's linked layout before testing it.");
        var layout =
            draft.Definition.MapLayouts.SingleOrDefault(l => l.Id == mission.LayoutId)
            ?? throw new InvalidOperationException("The mission layout is unavailable in this draft.");
        if (!string.Equals(layout.Location, session.Location, StringComparison.Ordinal))
            throw new InvalidOperationException("Open the mission layout's map before testing it.");
        var errors = MapLayoutRules.Errors(layout, walkthrough: true);
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
                return Response(state, "This mission attempt has already ended.", state.Completed, state.Completed);
            if (string.IsNullOrWhiteSpace(request.Kind))
                throw new InvalidOperationException("A mission progress kind is required.");
            if (string.IsNullOrWhiteSpace(request.OperationId))
                throw new InvalidOperationException("A stable mission progress operation identity is required.");

            var operationValue = request.Kind.Trim().ToLowerInvariant() + ":" + request.CheckpointId;
            if (state.ProgressOperations.TryGetValue(request.OperationId, out var previousValue))
            {
                if (!string.Equals(previousValue, operationValue, StringComparison.Ordinal))
                    throw new InvalidOperationException("The mission progress operation identity was reused for another transition.");
                return Response(state, "Mission progress already recorded.", state.Completed, state.Completed);
            }

            if (request.Kind.Equals("Checkpoint", StringComparison.OrdinalIgnoreCase))
            {
                if (!MissionRunRules.TryCheckpoint(state.Run, state.Layout.Checkpoints, request.CheckpointId, out var error))
                    throw new InvalidOperationException(error);
            }
            else if (request.Kind.Equals("Exit", StringComparison.OrdinalIgnoreCase))
            {
                if (!MissionRunRules.TryExit(state.Run, state.Layout.Checkpoints, state.Layout.Exit, request.CheckpointId, out var error))
                    throw new InvalidOperationException(error);
            }
            else
            {
                throw new InvalidOperationException("Unsupported mission test progress kind.");
            }

            state.ProgressOperations[request.OperationId] = operationValue;

            return Response(
                state,
                request.Kind.Equals("Exit", StringComparison.OrdinalIgnoreCase)
                    ? "Authored exit reached. Finalizing the mission test."
                    : "Checkpoint secured.",
                state.Completed
            );
        }
    }

    private EditorTestMissionResponse End(EditorSessionRegistry.Session session, EditorTestMissionRequest request)
    {
        var state = RequireRun(session, request);
        lock (state.Gate)
        {
            EnsureCurrentContent(state);
            if (state.Run.Status == MissionRunStatuses.Succeeded)
                return Response(state, "Mission test complete.", state.Completed, committed: true);
            if (state.Run.Status != MissionRunStatuses.Active)
                return Response(state, "Mission test failed.", state.Completed);
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
        if (state.ProfileId != session.Profile || state.DraftId != session.Draft || state.LayoutId != session.Layout)
            throw new InvalidOperationException("The editor mission test session changed.");
        state.LastTouched = DateTimeOffset.UtcNow;
        return state;
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
