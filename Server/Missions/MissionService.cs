using System.Security.Cryptography;
using Newtonsoft.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Bot;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Cloners;
using WTT.Campaigns.Server.Hub;
using WTT.Campaigns.Server.Profiles;
using WTT.Campaigns.Server.Seasons;
using WTT.Campaigns.Server.Story;
using WTT.Campaigns.Shared.Authoring;
using WTT.Campaigns.Shared.Missions;
using WTT.Campaigns.Shared.Seasons;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Server.Missions;

[Injectable(InjectionType.Singleton)]
public sealed class MissionService(
    SeasonService seasons,
    SeasonRepository repository,
    SaveServer saves,
    ICloner cloner,
    HubGameplay commits,
    StoryService story,
    JsonUtil json,
    BotController bots,
    WTT.Campaigns.Server.Editor.SceneContainerLoot? containers = null
)
{
    private static readonly TimeSpan PreparedLifetime = TimeSpan.FromMinutes(15);
    private const int MaxReceipts = 128;
    private const string MissionStart = "MissionStart";

    private sealed record Active(string Id, string Root, string SeasonId, SptProfile Profile, SeasonRuntimeSnapshot Runtime);

    private sealed record RaidCheckpoint(MissionCheckpoint Mission, SptProfile Profile);

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, RaidCheckpoint> _checkpoints = new();

    public async Task<MissionResponse> Read(string sessionId, MissionRequest request)
    {
        var root = seasons.ResolveRoot(sessionId);
        using var lease = seasons.Enter(root);
        var active = Resolve(sessionId, request);
        if (active.SeasonId != MissionLibrary.StandaloneScope && active.Runtime.Definition.MissionLinks.Count > 0)
        {
            var staged = cloner.Clone(active.Profile)!;
            story.RefreshMissionLinksUnderLease(active.Id, staged, active.SeasonId);
            if (
                JsonConvert.SerializeObject(staged.CharacterData!.PmcData!.ExtensionData)
                != JsonConvert.SerializeObject(active.Profile.CharacterData!.PmcData!.ExtensionData)
            )
                await commits.Commit(new MongoId(active.Id), active.Profile, staged);
            active = active with { Profile = staged };
        }
        var state = MissionStore.Read(active.Profile.CharacterData!.PmcData!, active.SeasonId);
        return Snapshot(active, state);
    }

    public async Task<MissionResponse> Prepare(string sessionId, MissionRequest request)
    {
        var root = seasons.ResolveRoot(sessionId);
        using var lease = seasons.Enter(root);
        var active = Resolve(sessionId, request);
        var original = active.Profile;
        var pmc = original.CharacterData!.PmcData!;
        var state = MissionStore.Read(original.CharacterData!.PmcData!, active.SeasonId);
        var fingerprint = Fingerprint("prepare", request);
        if (TryReplay(active, state, request.OperationId, fingerprint, includeDescriptor: true, out var replay))
            return replay;

        RequireOperation(request);
        RequireRevision(state, request.ExpectedRevision);
        // Preparing a mission is a profile mutation. Match the rest of the
        // campaign hub and reject it while this character/account is already
        // in an ordinary raid.
        seasons.EnsureNotInRaid(active.Id);
        var mission = FindMission(active.Runtime.Definition, request.MissionId);
        EnsureUnlocked(active, state, mission, pmc);
        if (state.ActiveRun is { } current && !MissionRunStatuses.IsTerminal(current.Status))
        {
            throw new InvalidOperationException("Finish or cancel the current mission before preparing another.");
        }

        var layout = ValidateMission(active.Runtime.Definition, mission);
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var run = new MissionRun
        {
            RunId = NewId(),
            CharacterId = active.Id,
            MissionId = mission.Id,
            LayoutId = layout.Id,
            ContextVersion = 3,
            Scope = active.SeasonId,
            PackageId = Link(active, mission.Id)?.Package.Id ?? "",
            PackageRevision = Link(active, mission.Id)?.Revision ?? 0,
            ContentRevision = ContentRevision(active, mission.Id),
            ContentHash = ContentHash(active, mission.Id),
            EncounterToken = NewId(),
            Status = MissionRunStatuses.Prepared,
            PreparedAt = now,
        };
        run.ContainerLoot = containers?.Create(layout) ?? new();
        if (layout.Objects.Any(o => SceneAssetRules.IsContainer(o)) && containers == null)
            throw new InvalidOperationException("Container generation service is unavailable.");
        state.ActiveRun = run;
        state.Revision++;
        AddReceipt(state, request.OperationId, fingerprint, "prepare", run, now);

        var staged = cloner.Clone(original)!;
        MissionStore.Write(staged.CharacterData!.PmcData!, state);
        await commits.Commit(new MongoId(active.Id), original, staged);
        return Response(active, state, "Mission prepared. Deploy to start the raid.", Descriptor(active, run));
    }

    public MissionResponse Descriptor(string sessionId, MissionRequest request)
    {
        var root = seasons.ResolveRoot(sessionId);
        using var lease = seasons.Enter(root);
        var active = Resolve(sessionId, request);
        var state = MissionStore.Read(active.Profile.CharacterData!.PmcData!, active.SeasonId);
        var run = RequireRun(state, request);
        VerifyCurrentContent(active, run);
        return Response(active, state, "", Descriptor(active, run));
    }

    public async Task<MissionResponse> Progress(string sessionId, MissionRequest request)
    {
        var root = seasons.ResolveRoot(sessionId);
        using var lease = seasons.Enter(root);
        var active = Resolve(sessionId, request);
        var original = active.Profile;
        var state = MissionStore.Read(original.CharacterData!.PmcData!, active.SeasonId);
        var fingerprint = Fingerprint("progress", request);
        if (TryReplay(active, state, request.OperationId, fingerprint, includeDescriptor: true, out var replay))
            return replay;

        RequireOperation(request);
        RequireRevision(state, request.ExpectedRevision);
        var run = RequireRun(state, request);
        if (run.Status != MissionRunStatuses.Active)
        {
            throw new InvalidOperationException("This mission raid is no longer active.");
        }
        VerifyCurrentContent(active, run);
        var mission = FindMission(active.Runtime.Definition, run.MissionId);
        var layout = ValidateMission(active.Runtime.Definition, mission);
        RequireRaidIdentity(active, run, request);

        var kind = request.Kind.Trim().ToLowerInvariant();
        if (request.AttemptGeneration != run.AttemptGeneration)
            throw new InvalidOperationException("This mission request belongs to a retired checkpoint attempt.");
        var changed = false;
        RaidCheckpoint? captured = null;
        SptProfile? rollback = null;
        if (kind == "start-checkpoint")
        {
            if (!mission.CheckpointRetries || run.Logic.Started || run.NextCheckpointIndex != 0 || _checkpoints.ContainsKey(run.RunId))
                throw new InvalidOperationException("The mission-start checkpoint cannot be captured now.");
            captured = new(new MissionCheckpoint(run, ""), cloner.Clone(original)!);
            changed = true;
        }
        else if (kind == "retry-prepare")
        {
            if (!run.PlayerDefeated && run.Logic.Failure.Length == 0)
                throw new InvalidOperationException("Only a failed mission attempt can retry its checkpoint.");
            if (!mission.CheckpointRetries || !_checkpoints.TryGetValue(run.RunId, out var saved))
                throw new InvalidOperationException("The raid-local checkpoint is unavailable.");
            run = saved.Mission.BeginRestore(run);
            run.RestoredActorIds = run.Logic.Actors.Values.Where(a => a.Spawned && !a.Dead).ToDictionary(a => a.ProfileId, _ => NewId());
            state.ActiveRun = run;
            rollback = cloner.Clone(saved.Profile)!;
            var checkpointProgress = MissionStore.Read(rollback.CharacterData!.PmcData!, active.SeasonId);
            state.CompletedMissionIds = checkpointProgress.CompletedMissionIds;
            state.UnlockedMissionIds = checkpointProgress.UnlockedMissionIds;
            changed = true;
        }
        else if (kind == "retry-commit")
        {
            if (!mission.CheckpointRetries || !_checkpoints.TryGetValue(run.RunId, out var saved))
                throw new InvalidOperationException("The raid-local checkpoint is unavailable.");
            saved.Mission.CommitRestore(mission, layout, run, run.RestoredActorIds);
            changed = true;
        }
        else if (kind == "defeat")
        {
            if (!mission.CheckpointRetries || run.Restoring || run.ExitReached)
                throw new InvalidOperationException("This mission cannot defer player defeat.");
            run.PlayerDefeated = true;
            changed = true;
        }
        else if (kind == "observations")
        {
            MissionObservationRules.Apply(mission, layout, run, request, DateTimeOffset.UtcNow.ToUnixTimeSeconds() - run.StartedAt);
            changed = true;
        }
        else if (kind == "checkpoint")
        {
            var wasCompleted = run.CompletedCheckpointIds.Contains(request.CheckpointId);
            if (!MissionLogic.CanAdvance(mission, run.Logic, request.CheckpointId, out var objectiveError))
                throw new InvalidOperationException(objectiveError);
            if (!MissionRunRules.TryCheckpoint(run, layout.Checkpoints, request.CheckpointId, out var checkpointError))
                throw new InvalidOperationException(checkpointError);
            if (wasCompleted)
            {
                return Response(active, state, "Checkpoint already reported.", Descriptor(active, run), committed: true);
            }
            changed = true;
            if (mission.CheckpointRetries)
                captured = new(new MissionCheckpoint(run, request.CheckpointId), cloner.Clone(original)!);
            MissionLogic.Apply(
                mission,
                layout,
                run.Logic,
                new MissionSignal
                {
                    Kind = MissionSignals.Checkpoint,
                    TargetId = request.CheckpointId,
                    Time = run.Logic.Time,
                }
            );
        }
        else if (kind == "exit")
        {
            if (!MissionLogic.CanAdvance(mission, run.Logic, "", out var objectiveError))
                throw new InvalidOperationException(objectiveError);
            var wasReached = run.ExitReached;
            if (!MissionRunRules.TryExit(run, layout.Checkpoints, layout.Exit, request.CheckpointId, out var exitError))
                throw new InvalidOperationException(exitError);
            changed = !wasReached;
            MissionLogic.Apply(mission, layout, run.Logic, new MissionSignal { Kind = MissionSignals.Exit, Time = run.Logic.Time });
        }
        else
        {
            throw new InvalidOperationException("Mission progress kind must be Checkpoint or Exit.");
        }

        if (!changed)
            return Response(active, state, "Mission progress already recorded.", Descriptor(active, run), committed: true);

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        state.Revision++;
        AddReceipt(state, request.OperationId, fingerprint, "progress", run, now);
        var staged = cloner.Clone(original)!;
        if (rollback != null)
            staged.CharacterData!.PmcData = rollback.CharacterData!.PmcData;
        MissionStore.Write(staged.CharacterData!.PmcData!, state);
        await commits.Commit(new MongoId(active.Id), original, staged);
        if (captured != null)
            _checkpoints[run.RunId] = captured;
        return Response(active, state, "Mission progress recorded.", Descriptor(active, run), committed: true);
    }

    public async Task<MissionResponse> Cancel(string sessionId, MissionRequest request)
    {
        var root = seasons.ResolveRoot(sessionId);
        using var lease = seasons.Enter(root);
        var active = Resolve(sessionId, request);
        var original = active.Profile;
        var state = MissionStore.Read(original.CharacterData!.PmcData!, active.SeasonId);
        var fingerprint = Fingerprint("cancel", request);
        if (TryReplay(active, state, request.OperationId, fingerprint, includeDescriptor: false, out var replay))
            return replay;

        RequireOperation(request);
        RequireRevision(state, request.ExpectedRevision);
        var run = RequireRun(state, request);
        if (request.AttemptGeneration != run.AttemptGeneration)
            throw new InvalidOperationException("This cancellation belongs to a retired mission attempt.");
        RequireMutationRun(run, request);
        if (run.Status == MissionRunStatuses.Active)
            RequireRaidIdentity(active, run, request);
        else if (request.RaidId.Length > 0 && request.RaidId != run.RaidId)
            throw new InvalidOperationException("This cancellation belongs to another raid.");
        if (MissionRunStatuses.IsTerminal(run.Status))
            return Response(active, state, "Mission run already ended.", null, committed: true);

        run.Status = MissionRunStatuses.Cancelled;
        run.FailureReason = "Mission run cancelled.";
        run.FinishedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        ClearTransientRunState(run);
        state.Revision++;
        AddReceipt(state, request.OperationId, fingerprint, "cancel", run, run.FinishedAt);
        var staged = cloner.Clone(original)!;
        MissionStore.Write(staged.CharacterData!.PmcData!, state);
        await commits.Commit(new MongoId(active.Id), original, staged);
        return Response(active, state, "Mission run cancelled.", null, committed: true);
    }

    /// <summary>
    /// Generates one bounded native bot-profile chunk for an authored roster. The
    /// request is checked before and after generation, and the result is cached in
    /// the run so retries cannot create extra profiles.
    /// </summary>
    public async Task<EditorEncounterProfilesResponse> EncounterProfiles(string sessionId, MissionEncounterProfilesRequest request)
    {
        var root = seasons.ResolveRoot(sessionId);
        string role;
        string difficulty;
        int count;
        string generationProfileId;
        string cacheKey;
        {
            using var lease = seasons.Enter(root);
            var active = Resolve(sessionId, request);
            var state = MissionStore.Read(active.Profile.CharacterData!.PmcData!, active.SeasonId);
            var run = RequireRun(state, request);
            RequireEncounterIdentity(active, run, request);
            var roster = FindRoster(active, run, request, out var encounter, out var wave);
            if (request.Offset < 0 || request.Count is < 1 or > 16 || request.Offset >= roster.Count)
                throw new InvalidOperationException("The encounter profile chunk is outside the authored roster.");
            var expectedCount = Math.Min(16, roster.Count - request.Offset);
            if (request.Offset % 16 != 0 || request.Count != expectedCount)
                throw new InvalidOperationException("Encounter profiles must be requested in canonical 16-profile chunks.");
            if (request.Offset + request.Count > roster.Count)
                throw new InvalidOperationException("The encounter profile chunk exceeds the authored roster count.");
            role = roster.Role;
            difficulty = roster.Difficulty;
            count = request.Count;
            generationProfileId = active.Id;
            cacheKey = ChunkKey(encounter.Id, wave.Id, roster.Id, request.Offset, request.Count);
            // A cached chunk is still content-bound. Do not let a stale retry
            // bypass the fixed revision/hash check simply because generation
            // completed during an earlier content revision.
            VerifyCurrentContent(active, run);
            if (run.EncounterProfileChunks.TryGetValue(cacheKey, out var cached))
                return new EditorEncounterProfilesResponse { ProfilesJson = cached };
        }

        var generated = (
            await bots.Generate(
                new MongoId(generationProfileId),
                new GenerateBotsRequestData
                {
                    Conditions =
                    [
                        new GenerateCondition
                        {
                            Role = role,
                            Difficulty = difficulty,
                            Limit = count,
                        },
                    ],
                }
            )
        ).Where(p => p != null).Take(count).ToArray();
        if (generated.Length != count)
            throw new InvalidOperationException("The installed bot generator returned an incomplete roster.");
        var profilesJson = json.Serialize(generated)!;

        using (var lease = seasons.Enter(root))
        {
            var active = Resolve(sessionId, request);
            var original = active.Profile;
            var state = MissionStore.Read(original.CharacterData!.PmcData!, active.SeasonId);
            var run = RequireRun(state, request);
            RequireEncounterIdentity(active, run, request);
            VerifyCurrentContent(active, run);
            if (run.EncounterProfileChunks.TryGetValue(cacheKey, out var cached))
                return new EditorEncounterProfilesResponse { ProfilesJson = cached };
            run.EncounterProfileChunks[cacheKey] = profilesJson;
            var authoredRoster = FindRoster(active, run, request, out var authoredEncounter, out var authoredWave);
            foreach (var profile in Newtonsoft.Json.Linq.JArray.Parse(profilesJson))
            {
                var profileId = (string?)profile["_id"] ?? throw new InvalidOperationException("Generated actor has no native identity.");
                run.Logic.Actors[profileId] = new MissionActor
                {
                    ProfileId = profileId,
                    EncounterId = authoredEncounter.Id,
                    WaveId = authoredWave.Id,
                    RosterId = authoredRoster.Id,
                    SquadId = authoredRoster.SquadId,
                };
            }
            var staged = cloner.Clone(original)!;
            MissionStore.Write(staged.CharacterData!.PmcData!, state);
            await commits.Commit(new MongoId(active.Id), original, staged);
            return new EditorEncounterProfilesResponse { ProfilesJson = profilesJson };
        }
    }

    /// <summary>
    /// Validates the server-issued launch marker before SPT creates a native
    /// raid. The prepared run, character and authored map must all match the
    /// request; a client-supplied body field cannot stand in for this marker.
    /// </summary>
    public void ValidateNativeLaunchMarker(string sessionId, string location, string runId)
    {
        if (string.IsNullOrWhiteSpace(runId))
            throw new InvalidOperationException("The mission launch marker is invalid.");

        var root = seasons.ResolveRoot(sessionId);
        using var lease = seasons.Enter(root);
        var active = ResolveSession(sessionId);
        var state = MissionStore.Read(active.Profile.CharacterData!.PmcData!, active.SeasonId);
        var run = state.ActiveRun;
        if (run == null || run.Status != MissionRunStatuses.Prepared || !string.Equals(run.RunId, runId, StringComparison.Ordinal))
            throw new InvalidOperationException("This mission launch is no longer prepared.");

        if (DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(run.PreparedAt) > PreparedLifetime)
            throw new InvalidOperationException("Mission preparation expired. Prepare the mission again.");

        var mission = FindMission(active.Runtime.Definition, run.MissionId);
        var layout = ValidateMission(active.Runtime.Definition, mission);
        if (!string.Equals(layout.Location, location, StringComparison.Ordinal))
            throw new InvalidOperationException("The selected raid map does not match the prepared mission.");
        VerifyCurrentContent(active, run);
        seasons.EnsureNotInRaid(active.Id);
    }

    /// <summary>
    /// Removes a prepared mission when the authenticated native start request
    /// carries no mission marker. This keeps an abandoned prepare from binding
    /// the next ordinary raid on the same map while preserving ordinary raid
    /// behavior for profiles without a prepared mission.
    /// </summary>
    public async Task CancelPreparedForOrdinaryStart(string sessionId)
    {
        if (!HasMissionState(sessionId))
            return;

        var root = seasons.ResolveRoot(sessionId);
        using var lease = seasons.Enter(root);
        var active = ResolveSession(sessionId);
        var original = active.Profile;
        var state = MissionStore.Read(original.CharacterData!.PmcData!, active.SeasonId);
        var run = state.ActiveRun;
        if (run == null || run.Status != MissionRunStatuses.Prepared)
            return;

        run.Status = MissionRunStatuses.Cancelled;
        run.FailureReason = "Mission launch marker was not present.";
        run.FinishedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        ClearTransientRunState(run);
        state.Revision++;
        var staged = cloner.Clone(original)!;
        MissionStore.Write(staged.CharacterData!.PmcData!, state);
        await commits.Commit(new MongoId(active.Id), original, staged);
    }

    /// <summary>Called after native quest acceptance, before SPT persists the profile.</summary>
    public void OnQuestAccepted(PmcData pmc, string seasonId, string questId)
    {
        if (!repository.Playable.TryGetValue(seasonId, out var runtime))
            return;
        // Harmony postfixes also run when a native controller returns an error
        // response. Only an accepted native quest may create a permanent
        // mission unlock.
        var questStatus = pmc.Quests?.FirstOrDefault(q => q.QId.ToString() == questId)?.Status.ToString();
        if (questStatus is not ("Started" or "AvailableForFinish" or "Success"))
            return;
        var definition = runtime.Definition;
        var missions = definition.Missions.Where(m => m.QuestId == questId).ToArray();
        var saved = MissionStore.Read(pmc, seasonId);
        foreach (var link in definition.MissionLinks)
        {
            if (
                link.UnlockTargetId == questId
                && (
                    link.Availability == MissionAvailability.QuestAccepted
                    || link.Availability == MissionAvailability.QuestCompleted && questStatus == "Success"
                )
                && saved.UnlockedMissionIds.Add(link.Id)
            )
                saved.Revision++;
            if (link.QuestId == questId && saved.CompletedMissionIds.Contains(link.Id))
                story.ApplyMissionCompletionUnderLease(
                    pmc,
                    seasonId,
                    new MissionDefinition { QuestId = link.QuestId, CompletionConditionId = link.CompletionConditionId }
                );
        }
        if (definition.MissionLinks.Count > 0)
        {
            MissionStore.Write(pmc, saved);
            story.RefreshMissionLinksUnderLease(pmc.Id.ToString(), new SptProfile { CharacterData = new() { PmcData = pmc } }, seasonId);
        }
        if (missions.Length == 0)
            return;
        var state = MissionStore.Read(pmc, seasonId);
        var changed = false;
        foreach (var mission in missions)
            changed |= MissionTransaction.Unlock(state, mission.Id, questStatus);
        if (changed)
        {
            state.Revision++;
            MissionStore.Write(pmc, state);
        }
    }

    /// <summary>Associates a prepared mission with SPT's server raid identity.</summary>
    public async Task StartRaid(string sessionId, StartLocalRaidRequestData request, StartLocalRaidResponseData response)
    {
        if (!HasMissionState(sessionId) || !string.Equals(request.PlayerSide, "pmc", StringComparison.OrdinalIgnoreCase))
            return;
        if (response.ServerId == null)
            return;

        var root = seasons.ResolveRoot(sessionId);
        using var lease = seasons.Enter(root);
        var active = ResolveSession(sessionId);
        var original = active.Profile;
        var pmc = original.CharacterData!.PmcData!;
        var state = MissionStore.Read(pmc, active.SeasonId);
        var run = state.ActiveRun;
        if (run == null || run.Status != MissionRunStatuses.Prepared)
            return;
        if (DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(run.PreparedAt) > PreparedLifetime)
        {
            run.Status = MissionRunStatuses.Cancelled;
            run.FailureReason = "Mission preparation expired.";
            run.FinishedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            ClearTransientRunState(run);
            state.Revision++;
            var expired = cloner.Clone(original)!;
            MissionStore.Write(expired.CharacterData!.PmcData!, state);
            await commits.Commit(new MongoId(active.Id), original, expired);
            throw new InvalidOperationException("Mission preparation expired. Prepare the mission again.");
        }

        var mission = FindMission(active.Runtime.Definition, run.MissionId);
        var layout = ValidateMission(active.Runtime.Definition, mission);
        if (!string.Equals(layout.Location, request.Location, StringComparison.Ordinal))
            throw new InvalidOperationException("The selected raid map does not match the prepared mission.");
        VerifyCurrentContent(active, run);
        run.RaidId = response.ServerId.ToString()!;
        run.Status = MissionRunStatuses.Active;
        run.StartedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        state.Revision++;
        var staged = cloner.Clone(original)!;
        MissionStore.Write(staged.CharacterData!.PmcData!, state);
        await commits.Commit(new MongoId(active.Id), original, staged);
    }

    /// <summary>Used by raid-end and recovery patches without acquiring a second character lease.</summary>
    public bool RaidFinished(string sessionId, string? raidId)
    {
        if (string.IsNullOrWhiteSpace(raidId) || !HasMissionState(sessionId))
            return false;
        var active = ResolveSession(sessionId);
        var run = MissionStore.Read(active.Profile.CharacterData!.PmcData!, active.SeasonId).ActiveRun;
        return run != null
            && run.NativeFinishCommitted
            && string.Equals(run.RaidId, raidId, StringComparison.Ordinal)
            && MissionRunStatuses.IsTerminal(run.Status);
    }

    /// <summary>Finalizes a mission after native inventory and raid reconciliation have run.</summary>
    public async Task FinishRaid(string sessionId, EndLocalRaidRequestData request, bool leaseHeld = false)
    {
        if (!HasMissionState(sessionId) || string.IsNullOrWhiteSpace(request.ServerId))
            return;
        var root = seasons.ResolveRoot(sessionId);
        using var lease = leaseHeld ? null : seasons.Enter(root);
        var active = ResolveSession(sessionId);
        var original = active.Profile;
        var pmc = original.CharacterData!.PmcData!;
        var state = MissionStore.Read(pmc, active.SeasonId);
        var run = state.ActiveRun;
        if (run == null || run.RaidId != request.ServerId || run.NativeFinishCommitted)
            return;

        // Cancel and recovery paths deliberately leave this bit clear. The
        // native EndLocalRaid call must still finish SPT inventory, loss and
        // profile reconciliation before repeated end requests are suppressed.
        if (MissionRunStatuses.IsTerminal(run.Status))
        {
            var terminal = cloner.Clone(original)!;
            var terminalPmc = terminal.CharacterData!.PmcData!;
            run.NativeFinishCommitted = true;
            ClearTransientRunState(run);
            state.Revision++;
            MissionStore.Write(terminalPmc, state);
            await commits.Commit(new MongoId(active.Id), original, terminal);
            return;
        }

        var staged = cloner.Clone(original)!;
        var stagedPmc = staged.CharacterData!.PmcData!;
        var contentCurrent = CurrentContent(active, run);
        if (!contentCurrent)
        {
            run.FinishedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            run.Status = MissionRunStatuses.Failed;
            run.FailureReason = "The mission content changed before finalization.";
            run.NativeFinishCommitted = true;
            ClearTransientRunState(run);
            state.Revision++;
            MissionStore.Write(stagedPmc, state);
            await commits.Commit(new MongoId(active.Id), original, staged);
            return;
        }

        var mission = FindMission(active.Runtime.Definition, run.MissionId);
        var layout = ValidateMission(active.Runtime.Definition, mission);
        var success = MissionRunRules.IsSuccessfulExtraction(
            run,
            layout.Checkpoints,
            layout.Exit,
            request.Results?.Result?.ToString(),
            request.Results?.ExitName,
            out var failureReason
        );
        if (success && !MissionLogic.CanAdvance(mission, run.Logic, "", out failureReason))
            success = false;

        run.FinishedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (success)
        {
            run.Status = MissionRunStatuses.Succeeded;
            run.FailureReason = "";
            state.CompletedMissionIds.Add(mission.Id);
            // The story adapter mutates the staged PMC and story extension state. Both
            // records are saved together below so a crash cannot split the receipt.
            if (mission.QuestId.Length > 0)
                story.ApplyMissionCompletionUnderLease(stagedPmc, active.SeasonId, mission);
        }
        else
        {
            run.Status = MissionRunStatuses.Failed;
            run.FailureReason = failureReason.Length > 0 ? failureReason : "The mission content changed before finalization.";
        }
        run.NativeFinishCommitted = true;
        ClearTransientRunState(run);
        state.Revision++;
        MissionStore.Write(stagedPmc, state);
        await commits.Commit(new MongoId(active.Id), original, staged);
    }

    /// <summary>Marks a prepared/active mission as failed when a client session is recovered.</summary>
    public async Task AbandonSession(string sessionId)
    {
        if (!HasMissionState(sessionId))
            return;
        var root = seasons.ResolveRoot(sessionId);
        using var lease = seasons.Enter(root);
        await AbandonSessionUnderLease(sessionId);
    }

    /// <summary>
    /// Fails a mission while an account-level abort route already owns the
    /// character lease. The route supplies the same authenticated character
    /// identity it used for the native abort validation, so no nested lease is
    /// acquired here.
    /// </summary>
    internal async Task AbandonSessionUnderLease(string sessionId, string? raidId = null)
    {
        if (!HasMissionState(sessionId))
            return;
        var active = ResolveSession(sessionId);
        var original = active.Profile;
        var state = MissionStore.Read(original.CharacterData!.PmcData!, active.SeasonId);
        var run = state.ActiveRun;
        if (run == null || MissionRunStatuses.IsTerminal(run.Status))
            return;
        if (!string.IsNullOrWhiteSpace(raidId) && !string.Equals(run.RaidId, raidId, StringComparison.Ordinal))
            return;
        run.Status = MissionRunStatuses.Failed;
        run.FailureReason = "The previous mission session ended before extraction.";
        run.FinishedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        ClearTransientRunState(run);
        state.Revision++;
        var staged = cloner.Clone(original)!;
        MissionStore.Write(staged.CharacterData!.PmcData!, state);
        await commits.Commit(new MongoId(active.Id), original, staged);
    }

    private Active Resolve(string sessionId, MissionRequest request)
    {
        var active = ResolveSession(sessionId);
        MissionTransaction.RequireRequestIdentity(request, active.Id, active.SeasonId);
        if (request.Version < 3 && (active.SeasonId == MissionLibrary.StandaloneScope || active.Runtime.Definition.MissionLinks.Count > 0))
            throw new InvalidOperationException("Update both mission components for independent missions.");
        if (request.Version < 2 && active.Runtime.Definition.Missions.Any(MissionLogic.HasLogic))
            throw new InvalidOperationException("Update both mission components for events and objectives.");
        return active;
    }

    private bool HasMissionState(string sessionId)
    {
        var id = seasons.EffectiveId(seasons.ResolveRoot(sessionId));
        return seasons.IsSeasonal(id)
            || saves
                .GetProfile(new MongoId(id))
                .CharacterData?.PmcData?.ExtensionData?.ContainsKey("wttCampaignsMissions:" + MissionLibrary.StandaloneScope) == true;
    }

    private Active ResolveSession(string sessionId)
    {
        var root = seasons.ResolveRoot(sessionId);
        var id = seasons.EffectiveId(root);

        var profile = saves.GetProfile(new MongoId(id));
        var pmc = profile.CharacterData?.PmcData ?? throw new InvalidOperationException("The Campaign profile is incomplete.");
        if (!seasons.IsSeasonal(id))
        {
            var previous = MissionStore.Read(pmc, MissionLibrary.StandaloneScope).ActiveRun;
            var definition = MissionLibrary.Standalone(repository.PublishedMissions.Values.Select(p => p.Definition), previous);
            return new Active(id, root, MissionLibrary.StandaloneScope, profile, new SeasonRuntimeSnapshot(definition));
        }
        var seasonId = seasons.SeasonIdFor(pmc);
        if (!repository.Playable.TryGetValue(seasonId, out var runtime))
            throw new InvalidOperationException("This campaign is unavailable.");
        return new Active(id, root, seasonId, profile, new SeasonRuntimeSnapshot(MissionLibrary.Resolve(runtime.Definition)));
    }

    private MissionResponse Snapshot(Active active, MissionProgress state)
    {
        var pmc = active.Profile.CharacterData!.PmcData!;
        var summaries = active
            .Runtime.Definition.Missions.Select(mission =>
            {
                var accepted =
                    pmc.Quests?.FirstOrDefault(q => q.QId.ToString() == mission.QuestId)?.Status.ToString()
                    is "Started"
                        or "AvailableForFinish"
                        or "Success";
                var link = Link(active, mission.Id);
                var unlocked = state.UnlockedMissionIds.Contains(mission.Id) || (link == null ? accepted : LinkEligible(active, link));
                var completed = state.CompletedMissionIds.Contains(mission.Id);
                var activeRun = state.ActiveRun is { } run && run.MissionId == mission.Id && !MissionRunStatuses.IsTerminal(run.Status);
                return new MissionSummary
                {
                    Definition = mission,
                    Status =
                        completed ? "Completed"
                        : activeRun ? "Active"
                        : unlocked ? "Available"
                        : "Locked",
                    Unlocked = unlocked,
                    LockReason =
                        unlocked ? ""
                        : link == null ? "Accept the linked quest to unlock this mission."
                        : MissionLibrary.LockReason(link),
                    Completed = completed,
                    Active = activeRun,
                    FailureReason =
                        state.ActiveRun is { MissionId: var id } failed && id == mission.Id && failed.Status == MissionRunStatuses.Failed
                            ? failed.FailureReason
                            : "",
                };
            })
            .ToList();
        if (
            state.ActiveRun is { } missing
            && !MissionRunStatuses.IsTerminal(missing.Status)
            && summaries.All(m => m.Definition.Id != missing.MissionId)
        )
            summaries.Add(
                new MissionSummary
                {
                    Definition = new MissionDefinition { Id = missing.MissionId, Name = "Unavailable mission" },
                    Active = true,
                    Status = "Unavailable",
                    FailureReason = "The pinned mission package is unavailable. Cancel this run before preparing another.",
                }
            );
        return new MissionResponse
        {
            SeasonId = active.SeasonId,
            CharacterId = active.Id,
            Revision = state.Revision,
            Missions = summaries,
            Run = state.ActiveRun,
            Message = "",
        };
    }

    private MissionResponse Response(
        Active active,
        MissionProgress state,
        string message,
        MissionDescriptor? descriptor,
        bool committed = false
    )
    {
        var output = Snapshot(active, state);
        output.Descriptor = descriptor;
        output.Message = message;
        output.Committed = committed;
        return output;
    }

    private MissionDescriptor Descriptor(Active active, MissionRun run)
    {
        var mission = FindMission(active.Runtime.Definition, run.MissionId);
        var layout = ValidateMission(active.Runtime.Definition, mission);
        var zones = active
            .Runtime.Definition.Zones.Where(z => z != null && (string.IsNullOrEmpty(z.LayoutId) || z.LayoutId == layout.Id))
            .Select(z => cloner.Clone(z)!)
            .ToList();
        return new MissionDescriptor
        {
            Scope = active.SeasonId,
            PackageId = run.PackageId,
            PackageRevision = run.PackageRevision,
            ContainerLoot = cloner.Clone(run.ContainerLoot)!,
            Definition = cloner.Clone(mission)!,
            Layout = cloner.Clone(layout)!,
            Zones = zones,
            CharacterId = active.Id,
            SessionId = active.Id,
            RunId = run.RunId,
            RaidId = run.RaidId,
            ContentRevision = run.ContentRevision,
            ContentHash = run.ContentHash,
            EncounterToken = run.EncounterToken,
            MissionOnlyExtracts = true,
        };
    }

    private static MissionDefinition FindMission(SeasonDefinition definition, string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidOperationException("Choose a mission.");
        return definition.Missions.SingleOrDefault(m => m.Id == id) ?? throw new InvalidOperationException("This mission is unavailable.");
    }

    private static MapLayout ValidateMission(SeasonDefinition definition, MissionDefinition mission)
    {
        var layout =
            definition.MapLayouts.SingleOrDefault(l => l.Id == mission.LayoutId)
            ?? throw new InvalidOperationException("The mission layout is unavailable.");
        if (layout.Start == null || layout.Exit == null || layout.Checkpoints is not { Count: > 0 })
            throw new InvalidOperationException("The mission layout requires a start, checkpoint sequence and exit.");
        var errors = MapLayoutRules.Errors(layout, walkthrough: true);
        errors.AddRange(MissionLogicRules.Errors(mission, layout));
        if (errors.Count > 0)
            throw new InvalidOperationException("The mission layout is invalid: " + errors[0]);
        if (layout.Encounters.Any(e => e.Trigger?.Type == MissionStart && e.Trigger.Volume != null))
            throw new InvalidOperationException("Mission-start encounters cannot have a trigger volume.");
        return layout;
    }

    private void EnsureUnlocked(Active active, MissionProgress state, MissionDefinition mission, PmcData pmc)
    {
        if (state.UnlockedMissionIds.Contains(mission.Id))
            return;
        var status = pmc.Quests?.FirstOrDefault(q => q.QId.ToString() == mission.QuestId)?.Status.ToString();
        var link = Link(active, mission.Id);
        if (link != null ? LinkEligible(active, link) : status is "Started" or "AvailableForFinish" or "Success")
        {
            state.UnlockedMissionIds.Add(mission.Id);
            return;
        }
        throw new InvalidOperationException("Accept the linked quest before deploying this mission.");
    }

    private static void RequireEncounterIdentity(Active active, MissionRun run, MissionEncounterProfilesRequest request)
    {
        if (
            run.Status != MissionRunStatuses.Active
            || run.Restoring
            || run.PlayerDefeated
            || request.AttemptGeneration != run.AttemptGeneration
            || !MissionRunRules.MatchesIdentity(run, active.Id, request.RunId, request.RaidId, request.EncounterToken)
        )
            throw new InvalidOperationException("The encounter request is not authorized for this mission raid.");
    }

    private static MapEncounterRosterEntry FindRoster(
        Active active,
        MissionRun run,
        MissionEncounterProfilesRequest request,
        out MapEncounter encounter,
        out MapEncounterWave wave
    )
    {
        var mission = FindMission(active.Runtime.Definition, run.MissionId);
        var layout = ValidateMission(active.Runtime.Definition, mission);
        encounter =
            layout.Encounters.SingleOrDefault(e => e.Id == request.EncounterId)
            ?? throw new InvalidOperationException("This encounter is not part of the mission layout.");
        wave =
            encounter.Waves.SingleOrDefault(w => w.Id == request.WaveId)
            ?? throw new InvalidOperationException("This encounter wave is not part of the mission layout.");
        return wave.Roster.SingleOrDefault(r => r.Id == request.RosterId)
            ?? throw new InvalidOperationException("This encounter roster is not part of the mission layout.");
    }

    private static string ChunkKey(string encounterId, string waveId, string rosterId, int offset, int count) =>
        string.Join("|", encounterId, waveId, rosterId, offset, count);

    private static MissionRun RequireRun(MissionProgress state, MissionRequest request)
    {
        var run = state.ActiveRun;
        if (run == null || string.IsNullOrWhiteSpace(request.RunId) || run.RunId != request.RunId)
            throw new InvalidOperationException("The mission run is unavailable. Prepare the mission again.");
        if (request.MissionId.Length > 0 && run.MissionId != request.MissionId)
            throw new InvalidOperationException("This mission request belongs to another run.");
        return run;
    }

    private static void RequireMutationRun(MissionRun run, MissionRequest request)
    {
        if (run == null || string.IsNullOrWhiteSpace(request.RunId) || !string.Equals(run.RunId, request.RunId, StringComparison.Ordinal))
            throw new InvalidOperationException("The mission run is unavailable. Prepare the mission again.");
    }

    private static void RequireRaidIdentity(Active active, MissionRun run, MissionRequest request)
    {
        if (!MissionRunRules.MatchesIdentity(run, active.Id, request.RunId, request.RaidId))
            throw new InvalidOperationException("This mission request belongs to another raid or character.");
    }

    private void ClearTransientRunState(MissionRun run)
    {
        _checkpoints.TryRemove(run.RunId, out _);
        run.EncounterProfileChunks?.Clear();
    }

    private static void VerifyCurrentContent(Active active, MissionRun run)
    {
        if (!CurrentContent(active, run))
            throw new InvalidOperationException("The mission content or context changed. Prepare it again.");
    }

    private static CampaignMissionLink? Link(Active active, string id) =>
        active.Runtime.Definition.MissionLinks.FirstOrDefault(l => l.Id == id);

    private bool LinkEligible(Active active, CampaignMissionLink link) =>
        active.SeasonId == MissionLibrary.StandaloneScope
            ? link.Package.MissionPackage?.AllowStandalonePlay == true
            : story.MissionLinkEligible(active.Id, active.Profile, active.SeasonId, link);

    private static long ContentRevision(Active active, string id) => Link(active, id)?.Revision ?? active.Runtime.Definition.Revision;

    private static string ContentHash(Active active, string id)
    {
        var definition = active.Runtime.Definition;
        var link = Link(active, id);
        if (link == null)
            return SeasonRepository.GameplayHash(definition);
        return SeasonRepository.Hash(
            System.Text.Encoding.UTF8.GetBytes(
                JsonConvert.SerializeObject(
                    new
                    {
                        Scope = active.SeasonId,
                        Link = link.Id,
                        link.MissionId,
                        link.Revision,
                        link.ContentHash,
                        link.Availability,
                        link.UnlockTargetId,
                        link.QuestId,
                        link.CompletionConditionId,
                    }
                )
            )
        );
    }

    private static bool CurrentContent(Active active, MissionRun run) =>
        run.ContextVersion == 3
            ? run.Scope == active.SeasonId
                && run.ContentRevision == ContentRevision(active, run.MissionId)
                && run.ContentHash == ContentHash(active, run.MissionId)
            : run.ContentRevision == active.Runtime.Definition.Revision
                && run.ContentHash == SeasonRepository.GameplayHash(active.Runtime.Definition);

    private static void RequireOperation(MissionRequest request)
    {
        MissionTransaction.RequireOperation(request);
    }

    private static void RequireRevision(MissionProgress state, long expected)
    {
        MissionTransaction.RequireRevision(state, expected);
    }

    private bool TryReplay(
        Active active,
        MissionProgress state,
        string operationId,
        string fingerprint,
        bool includeDescriptor,
        out MissionResponse response
    )
    {
        response = new MissionResponse();
        if (!MissionTransaction.TryReplayReceipt(state, operationId, fingerprint, out var receipt))
            return false;
        response = Snapshot(active, state);
        response.Replayed = true;
        response.Committed = true;
        if (includeDescriptor)
        {
            VerifyCurrentContent(active, state.ActiveRun);
            response.Descriptor = Descriptor(active, state.ActiveRun);
        }
        response.Message = "Mission operation already committed.";
        return true;
    }

    private void AddReceipt(MissionProgress state, string operationId, string fingerprint, string operation, MissionRun run, long timestamp)
    {
        MissionTransaction.AddReceipt(state, operationId, fingerprint, operation, run, timestamp, MaxReceipts);
    }

    private string Fingerprint(string operation, MissionRequest request)
    {
        return json.Serialize(
            new
            {
                operation,
                request.Version,
                request.SeasonId,
                request.CharacterId,
                request.MissionId,
                request.RunId,
                request.RaidId,
                request.CheckpointId,
                request.Kind,
                request.AttemptGeneration,
                request.Signals,
            }
        )!;
    }

    private static string NewId() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(12));
}
