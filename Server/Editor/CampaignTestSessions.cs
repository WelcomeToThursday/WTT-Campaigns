using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services.Profile;
using WTT.Campaigns.Server.Hub;
using WTT.Campaigns.Server.Missions;
using WTT.Campaigns.Server.Profiles;
using WTT.Campaigns.Server.Seasons;
using WTT.Campaigns.Server.Story;
using WTT.Campaigns.Shared.Authoring;
using WTT.Campaigns.Shared.Missions;
using WTT.Campaigns.Shared.Seasons;

namespace WTT.Campaigns.Server.Editor;

/// <summary>
/// Owns full campaign rehearsals started by Campaign Editor. Each rehearsal
/// receives a duplicated, isolated campaign snapshot and a normal native PMC;
/// only the control endpoints are editor-authenticated. The profile and its
/// account link are process-local and are tombstoned when retired so late
/// native saves can never create a file or revive the test.
/// </summary>
[Injectable(InjectionType.Singleton)]
public sealed class CampaignTestSessions(
    SaveServer saves,
    CreateProfileService creator,
    TemplateTable templates,
    SeasonRepository repository,
    SeasonService seasons,
    SeasonStartingService starting,
    HubQuestService hubQuests,
    HubGameplay hub,
    StoryService story,
    MissionService missions,
    SeasonContentService content
)
{
    private sealed class TestState
    {
        public string EditorSessionId { get; init; } = "";
        public string Owner { get; init; } = "";
        public string DraftId { get; init; } = "";
        public long SourceRevision { get; init; }
        public string SourceHash { get; init; } = "";
        public string TestId { get; init; } = "";
        public string SeasonId { get; init; } = "";
        public string ReturnProfileId { get; init; } = "";
        public string LayoutId { get; init; } = "";
        public string MissionId { get; init; } = "";
        public string QuestId { get; init; } = "";

        // Campaign test control traffic heartbeats this value. Recovery is
        // request-driven, so an abandoned test cannot retain native
        // registrations indefinitely when its editor process disappears.
        public DateTimeOffset Contact { get; set; } = DateTimeOffset.UtcNow;
        public IDisposable SnapshotRegistration { get; init; } = null!;
        public IDisposable ContentRegistration { get; init; } = null!;
        public IDisposable QuestRegistration { get; init; } = null!;
        public IDisposable HubRegistration { get; init; } = null!;
    }

    private readonly ConcurrentDictionary<string, TestState> _tests = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _sessionTests = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _successors = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CampaignTestResponse> _ended = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _endedOwners = new(StringComparer.Ordinal);
    private static readonly TimeSpan AbandonedLifetime = TimeSpan.FromMinutes(5);

    // Includes retired ids. Native SaveServer callbacks can arrive after an
    // end/reset request, so tombstones must outlive the in-memory resources.
    private static readonly ConcurrentDictionary<string, byte> TestIds = new(StringComparer.Ordinal);

    public static bool IsTest(string id) => !string.IsNullOrWhiteSpace(id) && TestIds.ContainsKey(id);

    public async Task<CampaignTestResponse> Create(string transportIdentity, CampaignTestRequest request)
    {
        ValidateRequest(request, CampaignTestActions.Create);

        if (request.TestId.Length > 0)
        {
            Touch(request.TestId, transportIdentity);
            await RecoverAbandoned(transportIdentity);
            if (_ended.TryGetValue(request.TestId, out var ended))
                return OwnedEnded(transportIdentity, ended);
            var existingById = ResolveActive(request.TestId, transportIdentity);
            if (existingById != null)
                return await Projection(existingById, replayed: true);
            throw new InvalidOperationException("The disposable campaign test is unavailable. Create it again.");
        }

        await RecoverAbandoned(transportIdentity);
        var session = ResolveEditor(transportIdentity, request.EditorSessionId, request.DraftId);
        if (session.Draft.Length > 0 && session.Draft != request.DraftId)
            throw new InvalidOperationException("The selected editor draft changed. Refresh the editor and try again.");

        if (_sessionTests.TryGetValue(request.EditorSessionId, out var existingId))
        {
            if (_tests.TryGetValue(existingId, out var existing))
            {
                if (existing.DraftId != request.DraftId)
                    throw new InvalidOperationException("End the current disposable test before testing another draft.");
                return await Projection(existing, replayed: true);
            }
            _sessionTests.TryRemove(request.EditorSessionId, out _);
        }

        using var lease = seasons.Enter(session.Owner);
        // Recheck under the owner lease so two create retries cannot allocate
        // two native profiles for the same editor session.
        if (_sessionTests.TryGetValue(request.EditorSessionId, out existingId) && _tests.TryGetValue(existingId, out var concurrent))
            return await Projection(concurrent, replayed: true);

        var draft = LoadDraft(request.DraftId);
        var state = await Build(session, draft);
        _tests[state.TestId] = state;
        _sessionTests[state.EditorSessionId] = state.TestId;
        return await Projection(state, committed: true);
    }

    public async Task<CampaignTestResponse> Status(string transportIdentity, CampaignTestRequest request)
    {
        ValidateRequest(request, CampaignTestActions.Status);
        Touch(request.TestId, transportIdentity);
        await RecoverAbandoned(transportIdentity);
        if (_ended.TryGetValue(request.TestId, out var ended))
            return OwnedEnded(transportIdentity, ended);
        var state = RequireActive(request, transportIdentity);
        return await Projection(state);
    }

    public async Task<CampaignTestResponse> Reset(string transportIdentity, CampaignTestRequest request)
    {
        ValidateRequest(request, CampaignTestActions.Reset);
        Touch(request.TestId, transportIdentity);
        await RecoverAbandoned(transportIdentity);
        if (_ended.ContainsKey(request.TestId))
            throw new InvalidOperationException("The disposable campaign test has ended. Create a new test.");
        var old = RequireActive(request, transportIdentity);
        // A lost reset response is replayed through the old-id successor map.
        // Do not reset the fresh successor a second time.
        if (old.TestId != request.TestId)
            return await Projection(old, replayed: true);
        using var lease = seasons.Enter(old.TestId);
        seasons.EnsureNotInRaid(old.TestId);

        var draft = LoadDraft(old.DraftId);
        var replacement = await Build(new EditorSessionIdentity(old.EditorSessionId, old.Owner, old.ReturnProfileId, old.DraftId), draft);
        try
        {
            await Retire(old);
            _tests.TryRemove(old.TestId, out _);
            _tests[replacement.TestId] = replacement;
            _sessionTests[replacement.EditorSessionId] = replacement.TestId;
            _successors[old.TestId] = replacement.TestId;
            return await Projection(replacement, committed: true);
        }
        catch
        {
            await Retire(replacement);
            throw;
        }
    }

    public async Task<CampaignTestResponse> End(string transportIdentity, CampaignTestRequest request)
    {
        ValidateRequest(request, CampaignTestActions.End);
        Touch(request.TestId, transportIdentity);
        await RecoverAbandoned(transportIdentity);
        if (_ended.TryGetValue(request.TestId, out var ended))
            return OwnedEnded(transportIdentity, ended);
        var state = RequireActive(request, transportIdentity);
        using var lease = seasons.Enter(state.TestId);
        seasons.EnsureNotInRaid(state.TestId);
        await Retire(state);
        var response = new CampaignTestResponse
        {
            EditorSessionId = state.EditorSessionId,
            DraftId = state.DraftId,
            TestId = state.TestId,
            ProfileId = state.TestId,
            SeasonId = state.SeasonId,
            ReturnProfileId = state.ReturnProfileId,
            LayoutId = state.LayoutId,
            MissionId = state.MissionId,
            QuestId = state.QuestId,
            Status = "Ended",
            Message = "Disposable campaign test ended.",
            SourcePreserved = SourcePreserved(state),
            Disposable = true,
            Committed = true,
        };
        _endedOwners[state.TestId] = state.Owner;
        _ended[state.TestId] = response;
        _tests.TryRemove(state.TestId, out _);
        _sessionTests.TryRemove(new KeyValuePair<string, string>(state.EditorSessionId, state.TestId));
        return response;
    }

    private async Task<TestState> Build(EditorSessionIdentity session, DraftEnvelope draft)
    {
        var source = draft.Definition;
        var validation = SeasonValidator.Validate(source);
        if (!validation.CanActivate)
            throw new InvalidOperationException("Fix the draft before starting a campaign test: " + validation.Issues[0].Message);
        if (source.Missions.Count == 0)
            throw new InvalidOperationException("Add a mission to this draft before starting a campaign test.");

        // Keep the chosen mission/layout by ordinal: Duplicate rewrites all
        // owned ids, including mission, layout, quest, story and zone links.
        var sourceMission = source.Missions[0];
        var sourceLayoutIndex = source.MapLayouts.FindIndex(l => l.Id == sourceMission.LayoutId);
        if (sourceLayoutIndex < 0)
            throw new InvalidOperationException("The mission layout is unavailable in this draft.");

        var copy = SeasonRepository.Duplicate(source);
        var mission = copy.Missions.ElementAtOrDefault(source.Missions.IndexOf(sourceMission));
        var layout = copy.MapLayouts.ElementAtOrDefault(sourceLayoutIndex);
        if (mission == null || layout == null || mission.LayoutId != layout.Id)
            throw new InvalidOperationException("The duplicated mission layout could not be resolved.");

        var snapshotRegistration = repository.RegisterIsolatedSnapshot(copy);
        IDisposable? contentRegistration = null;
        IDisposable? questRegistration = null;
        IDisposable? hubRegistration = null;
        var id = SeasonRepository.NewId();
        TestIds[id] = 0;
        try
        {
            contentRegistration = content.RegisterIsolated(snapshotRegistration.Snapshot.Definition);
            questRegistration = hubQuests.RegisterIsolated(snapshotRegistration.Snapshot.Definition);
            hubRegistration = hub.RegisterIsolated(snapshotRegistration.Snapshot);
            // Register the self-owned account link before native profile
            // creation. SPT profile hooks may inspect campaign identity while
            // constructing the PMC and must never fall through to disk-backed
            // launcher-link discovery for this temporary id.
            seasons.RegisterEphemeral(id, copy.Id);
            await CreateProfile(id, session.Owner, snapshotRegistration.Snapshot.Definition, mission.QuestId);
            var pmc = saves.GetProfile(new MongoId(id)).CharacterData!.PmcData!;
            seasons.InitializeEphemeralProfile(pmc, id, copy.Id);
            AddQuest(pmc, mission.QuestId);
            await story.NewSession(id);
            return new TestState
            {
                EditorSessionId = session.EditorSessionId,
                Owner = session.Owner,
                DraftId = draft.Id,
                SourceRevision = draft.Revision,
                SourceHash = Hash(source),
                TestId = id,
                SeasonId = copy.Id,
                ReturnProfileId = session.ReturnProfileId,
                LayoutId = layout.Id,
                MissionId = mission.Id,
                QuestId = mission.QuestId,
                Contact = DateTimeOffset.UtcNow,
                SnapshotRegistration = snapshotRegistration,
                ContentRegistration = contentRegistration,
                QuestRegistration = questRegistration,
                HubRegistration = hubRegistration,
            };
        }
        catch
        {
            if (seasons.IsEphemeral(id))
                seasons.UnregisterEphemeral(id);
            saves.GetProfiles().Remove(new MongoId(id));
            TestIds[id] = 0;
            hubRegistration?.Dispose();
            questRegistration?.Dispose();
            contentRegistration?.Dispose();
            snapshotRegistration.Dispose();
            throw;
        }
    }

    private async Task CreateProfile(string id, string owner, SeasonDefinition season, string questId)
    {
        var ownerProfile = saves.GetProfile(new MongoId(owner));
        var cosmetic = templates
            .Customization.Values.Where(v =>
                v.Parent is "5cc085e214c02e000c6bea67" or "5fc100cf95572123ae738483"
                && v.Properties.AvailableAsDefault
                && v.Properties.Side.Contains("Usec")
            )
            .GroupBy(v => v.Parent)
            .ToDictionary(g => g.Key, g => g.OrderBy(v => v.Id.ToString(), StringComparer.Ordinal).First().Id);
        if (
            !cosmetic.TryGetValue("5cc085e214c02e000c6bea67", out var head)
            || !cosmetic.TryGetValue("5fc100cf95572123ae738483", out var voice)
        )
            throw new InvalidOperationException("SPT has no default PMC appearance for a campaign test.");

        saves.CreateProfile(
            new SPTarkov.Server.Core.Models.Eft.Profile.Info
            {
                ProfileId = new MongoId(id),
                ScavengerId = new MongoId(SeasonRepository.NewId()),
                Aid = saves.GetProfiles().Values.Max(p => p.ProfileInfo?.Aid ?? 0) + 1,
                Username = "CampaignTest",
                Edition = ownerProfile.ProfileInfo!.Edition,
                IsWiped = false,
            }
        );
        await creator.CreateProfile(
            new MongoId(id),
            new ProfileCreateRequestData
            {
                Side = "Usec",
                Nickname = "CampaignTest",
                HeadId = head,
                VoiceId = voice,
            }
        );
        await starting.Apply(id, "Usec", season.Id);
    }

    private void AddQuest(PmcData pmc, string questId)
    {
        pmc.Quests ??= [];
        var status = pmc.Quests.FirstOrDefault(q => q.QId.ToString() == questId);
        if (status == null)
        {
            pmc.Quests.Add(
                status = new QuestStatus
                {
                    QId = new MongoId(questId),
                    StartTime = 0,
                    Status = QuestStatusEnum.AvailableForStart,
                    StatusTimers = new(),
                    CompletedConditions = [],
                }
            );
        }
        else
        {
            status.Status = QuestStatusEnum.AvailableForStart;
            status.CompletedConditions ??= [];
            status.StatusTimers ??= new();
        }
    }

    private async Task Retire(TestState state)
    {
        // Clear story transient state while the test profile still resolves as
        // a seasonal character, then release native/runtime registrations.
        if (seasons.IsEphemeral(state.TestId))
        {
            try
            {
                await story.ResetSessionUnderLease(state.TestId);
            }
            catch
            {
                // Teardown must continue even if an already-aborted story has
                // no state left to reset.
            }
            seasons.UnregisterEphemeral(state.TestId);
        }
        saves.GetProfiles().Remove(new MongoId(state.TestId));
        state.HubRegistration.Dispose();
        state.QuestRegistration.Dispose();
        state.ContentRegistration.Dispose();
        state.SnapshotRegistration.Dispose();
        TestIds[state.TestId] = 0;
    }

    /// <summary>
    /// Retires stale disposable tests when their owner next contacts the
    /// server. Native raid profiles are left alone while a raid is active;
    /// the normal raid-abort/end hooks then perform their ordinary cleanup.
    /// </summary>
    public async Task RecoverAbandoned(string identity)
    {
        if (string.IsNullOrWhiteSpace(identity))
            return;

        var cutoff = DateTimeOffset.UtcNow - AbandonedLifetime;
        foreach (var state in _tests.Values.Where(s => s.Owner == identity || s.ReturnProfileId == identity).ToArray())
        {
            if (state.Contact >= cutoff || !_tests.TryGetValue(state.TestId, out var current) || !ReferenceEquals(current, state))
                continue;

            try
            {
                using var lease = seasons.Enter(state.TestId);
                if (!_tests.TryGetValue(state.TestId, out current) || !ReferenceEquals(current, state) || state.Contact >= cutoff)
                    continue;

                // A stalled editor heartbeat must not tear down a real native
                // raid. Such a test is recovered by the normal raid-abort/end
                // path and can be retired on the next control request.
                seasons.EnsureNotInRaid(state.TestId);
                var response = AbandonedResponse(state);
                await Retire(state);
                _tests.TryRemove(state.TestId, out _);
                _sessionTests.TryRemove(new KeyValuePair<string, string>(state.EditorSessionId, state.TestId));
                _endedOwners[state.TestId] = state.Owner;
                _ended[state.TestId] = response;
            }
            catch (InvalidOperationException)
            {
                // The profile is currently in a raid or no longer resolves;
                // leave it for native lifecycle cleanup and retry later.
            }
        }
    }

    private CampaignTestResponse AbandonedResponse(TestState state) =>
        new()
        {
            EditorSessionId = state.EditorSessionId,
            DraftId = state.DraftId,
            TestId = state.TestId,
            ProfileId = state.TestId,
            SeasonId = state.SeasonId,
            ReturnProfileId = state.ReturnProfileId,
            LayoutId = state.LayoutId,
            MissionId = state.MissionId,
            QuestId = state.QuestId,
            Status = "Abandoned",
            Message = "The disposable campaign test was recovered after its owner stopped contacting the server.",
            SourcePreserved = SourcePreserved(state),
            Disposable = true,
            Committed = true,
        };

    private void Touch(string testId, string identity)
    {
        if (!_tests.TryGetValue(testId, out var state))
            return;
        if (identity == state.Owner || identity == state.ReturnProfileId || EditorIdentityMatches(state.EditorSessionId, identity))
            state.Contact = DateTimeOffset.UtcNow;
    }

    private async Task<CampaignTestResponse> Projection(TestState state, bool replayed = false, bool committed = false)
    {
        var profile = saves.GetProfile(new MongoId(state.TestId));
        var pmc = profile.CharacterData!.PmcData!;
        var campaignState = SeasonService.State(pmc);
        var quest = pmc.Quests?.FirstOrDefault(q => q.QId.ToString() == state.QuestId);
        var missionState = MissionStore.Read(pmc, state.SeasonId);
        MissionResponse? missionResponse = null;
        try
        {
            missionResponse = missions.Read(
                state.TestId,
                new MissionRequest
                {
                    Version = 1,
                    SeasonId = state.SeasonId,
                    CharacterId = state.TestId,
                }
            );
        }
        catch (InvalidOperationException)
        {
            // A profile is still useful to the client if native startup has
            // not populated mission state yet; the next heartbeat will retry.
        }

        var completed = missionState.CompletedMissionIds.Contains(state.MissionId);
        var inRaid = !string.IsNullOrEmpty(profile.InraidData?.Location) && profile.InraidData.Location != "none";
        return new CampaignTestResponse
        {
            EditorSessionId = state.EditorSessionId,
            DraftId = state.DraftId,
            TestId = state.TestId,
            ProfileId = state.TestId,
            SeasonId = state.SeasonId,
            ReturnProfileId = state.ReturnProfileId,
            LayoutId = state.LayoutId,
            MissionId = state.MissionId,
            QuestId = state.QuestId,
            RunId = missionState.ActiveRun?.RunId ?? "",
            Revision = Math.Max(campaignState.Revision, missionState.Revision),
            Status =
                inRaid ? "InRaid"
                : completed ? "Completed"
                : "Ready",
            Message = completed
                ? "Mission complete. Turn in the native quest or replay it."
                : "Disposable native campaign profile is ready.",
            QuestAccepted = quest?.Status is QuestStatusEnum.Started or QuestStatusEnum.AvailableForFinish or QuestStatusEnum.Success,
            MissionCompleted = completed,
            QuestCompleted = quest?.Status == QuestStatusEnum.Success,
            ReplayReady = completed,
            Replayed = replayed,
            Committed = committed,
            Disposable = true,
            SourcePreserved = SourcePreserved(state),
            Snapshot = seasons.GetSnapshot(state.TestId, state.SeasonId, state.TestId),
            Missions = missionResponse,
        };
    }

    private bool SourcePreserved(TestState state)
    {
        try
        {
            var current = repository.Load(state.DraftId);
            return current.Revision == state.SourceRevision && Hash(current.Definition) == state.SourceHash;
        }
        catch
        {
            return false;
        }
    }

    private CampaignTestResponse OwnedEnded(string transportIdentity, CampaignTestResponse response)
    {
        if (
            transportIdentity != response.ReturnProfileId
            && (!_endedOwners.TryGetValue(response.TestId, out var owner) || transportIdentity != owner)
        )
            throw new InvalidOperationException("The disposable campaign test does not belong to this account.");
        var copy = JsonConvert.DeserializeObject<CampaignTestResponse>(JsonConvert.SerializeObject(response))!;
        copy.Replayed = true;
        return copy;
    }

    private TestState RequireActive(CampaignTestRequest request, string transportIdentity)
    {
        if (request.TestId.Length == 0)
            throw new InvalidOperationException("A disposable campaign test id is required.");
        var state =
            ResolveActive(request.TestId, transportIdentity)
            ?? throw new InvalidOperationException("The disposable campaign test is unavailable. Create it again.");
        if (request.EditorSessionId.Length > 0 && request.EditorSessionId != state.EditorSessionId)
            throw new InvalidOperationException("The editor session does not belong to this disposable test.");
        if (request.DraftId.Length > 0 && request.DraftId != state.DraftId)
            throw new InvalidOperationException("The draft does not belong to this disposable test.");
        return state;
    }

    private TestState? ResolveActive(string id, string transportIdentity)
    {
        var candidate = id;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            if (_tests.TryGetValue(candidate, out var state))
            {
                Authorize(state, transportIdentity);
                return state;
            }
            if (!_successors.TryGetValue(candidate, out candidate!))
                return null;
        }
        throw new InvalidOperationException("The disposable campaign test identity chain is invalid.");
    }

    private void Authorize(TestState state, string identity)
    {
        if (identity != state.Owner && identity != state.ReturnProfileId && !EditorIdentityMatches(state.EditorSessionId, identity))
            throw new InvalidOperationException("The disposable campaign test does not belong to this account.");
    }

    private static bool EditorIdentityMatches(string sessionId, string identity)
    {
        var resolution = EditorSessionRegistry.Resolve(identity, sessionId, DateTimeOffset.UtcNow);
        return resolution.Session != null
            && resolution.Status is (EditorSessionRegistry.ResolutionStatus.Accepted or EditorSessionRegistry.ResolutionStatus.MissingMap);
    }

    private static EditorSessionRegistry.Session ResolveEditor(string identity, string sessionId, string draftId)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || !SeasonValidator.IsId(draftId))
            throw new InvalidOperationException("Choose an editor session and saved draft before starting a campaign test.");
        var resolution = EditorSessionRegistry.Resolve(identity, sessionId, DateTimeOffset.UtcNow);
        if (
            resolution.Session == null
            || resolution.Status
                is not (EditorSessionRegistry.ResolutionStatus.Accepted or EditorSessionRegistry.ResolutionStatus.MissingMap)
        )
            throw new InvalidOperationException(
                resolution.Status
                    is EditorSessionRegistry.ResolutionStatus.Expired
                        or EditorSessionRegistry.ResolutionStatus.NotReady
                        or EditorSessionRegistry.ResolutionStatus.RetiredScratch
                    ? "Editor session expired. Return to editor home and reconnect."
                    : "Editor session does not match."
            );
        var session = resolution.Session;
        session.Contact = DateTimeOffset.UtcNow;
        return session;
    }

    private static void ValidateRequest(CampaignTestRequest request, string action)
    {
        if (request.Version != 1)
            throw new InvalidOperationException("Update both editor components together (campaign test protocol 1 required).");
        if (action != CampaignTestActions.Create && !SeasonValidator.IsId(request.TestId))
            throw new InvalidOperationException("A valid disposable campaign test id is required.");
    }

    private DraftEnvelope LoadDraft(string id)
    {
        if (!SeasonValidator.IsId(id))
            throw new InvalidOperationException("The selected draft identity is invalid.");
        var draft = repository.Load(id);
        if (draft.Status != DraftStatus.Active)
            throw new InvalidOperationException("Restore this draft before starting a campaign test.");
        return draft;
    }

    private static string Hash(SeasonDefinition definition) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(definition))));

    private sealed record EditorSessionIdentity(string EditorSessionId, string Owner, string ReturnProfileId, string DraftId)
    {
        public static implicit operator EditorSessionIdentity(EditorSessionRegistry.Session session) =>
            new(session.Id, session.Owner, session.ReturnProfile, session.Draft);
    }
}

/// <summary>DTO adapter required by SPT's StaticRouter generic boundary.</summary>
public sealed class CampaignTestRouteRequest : CampaignTestRequest, SPTarkov.Server.Core.Models.Utils.IRequestData;
