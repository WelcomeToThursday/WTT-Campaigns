using System.Collections.Concurrent;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services.Profile;
using SPTarkov.Server.Core.Utils;
using WTT.Campaigns.Server.Hub;
using WTT.Campaigns.Server.Profiles;
using WTT.Campaigns.Server.Seasons;
using WTT.Campaigns.Server.Story;
using WTT.Campaigns.Shared.Authoring;
using WTT.Campaigns.Shared.Seasons;

namespace WTT.Campaigns.Server.Editor;

[Injectable(InjectionType.Singleton)]
public sealed partial class CampaignTestSessions(
    SaveServer saves,
    CreateProfileService creator,
    TemplateTable templates,
    SeasonRepository repository,
    SeasonService seasons,
    SeasonStartingService starting,
    HubQuestService hubQuests,
    HubGameplay hub,
    StoryService story,
    SeasonContentService content,
    JsonUtil json,
    SPTarkov.Server.Core.Routers.ImageRouter images
)
{
    private sealed class TestState
    {
        public CampaignTestRecord Record { get; set; } = new();
        public string EditorSessionId { get; set; } = "";
        public string ReturnProfileId { get; set; } = "";
        public List<IDisposable> Registrations { get; } = new();
        public bool Saving { get; set; }
    }

    private readonly CampaignTestStorage _storage = new(Path.GetFullPath("user/seasonal/campaign-tests"));
    private readonly ConcurrentDictionary<string, TestState> _tests = new();
    private readonly ConcurrentDictionary<string, (string Owner, CampaignTestResponse Response)> _ended = new();
    private static readonly ConcurrentDictionary<string, byte> TestIds = new();
    private static readonly ConcurrentDictionary<string, Action> SaveHandlers = new();

    public static bool IsTest(string id) => TestIds.ContainsKey(id);

    public static Task<long> SaveTest(string id)
    {
        if (SaveHandlers.TryGetValue(id, out var save))
            save();
        return Task.FromResult(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    public Task<CampaignTestResponse> List(string identity, CampaignTestRequest request)
    {
        CampaignTestPolicy.RequireRequest(request, CampaignTestActions.List);
        var owner = Owner(identity);
        return Task.FromResult(
            new CampaignTestResponse
            {
                Drafts = repository
                    .Drafts()
                    .Where(d => d.Definition.MissionPackage == null && !d.Definition.Legacy)
                    .Select(d => new CampaignTestDraft
                    {
                        Id = d.Id,
                        Name = d.Definition.Name,
                        Revision = d.Revision,
                        HasProgress = _storage.Read(owner, d.Id) != null,
                    })
                    .ToList(),
            }
        );
    }

    public async Task<CampaignTestResponse> Create(string identity, CampaignTestRequest request)
    {
        CampaignTestPolicy.RequireRequest(request, CampaignTestActions.Create, CampaignTestActions.Resume);
        var editor = request.EditorSessionId.Length > 0 ? ResolveEditor(identity, request.EditorSessionId, request.DraftId) : null;
        var owner = editor?.Owner ?? Owner(identity);
        var returnId = editor?.ReturnProfile ?? seasons.EffectiveId(owner);
        using var lease = seasons.Enter(owner);
        if (saves.GetProfile(returnId).CharacterData?.PmcData != null)
            seasons.EnsureNotInRaid(returnId);
        var existing = _tests.Values.FirstOrDefault(s => s.Record.Owner == owner);
        if (existing != null)
        {
            if (existing.Record.DraftId != request.DraftId)
                throw new InvalidOperationException("Return from the current campaign test first.");
            using var testLease = seasons.Enter(existing.Record.ProfileId);
            seasons.EnsureNotInRaid(existing.Record.ProfileId);
            // The client may have closed while the server kept the test loaded.
            // Bind its controls to the newly authenticated menu/editor visit.
            existing.EditorSessionId = request.EditorSessionId;
            existing.ReturnProfileId = returnId;
            return Projection(existing);
        }
        var draft = LoadDraft(request.DraftId);
        var saved = _storage.Read(owner, draft.Id);
        if (saved == null && request.ExpectedDraftRevision != draft.Revision)
            throw new InvalidOperationException("The draft changed. Refresh the draft list and try again.");
        var record = saved ?? NewRecord(owner, draft);
        foreach (var retired in record.RetiredProfiles)
            TestIds[retired] = 0;
        var state = new TestState
        {
            Record = record,
            EditorSessionId = request.EditorSessionId,
            ReturnProfileId = returnId,
        };
        TestIds[record.ProfileId] = 0;
        try
        {
            Register(state);
            seasons.RegisterEphemeral(record.ProfileId, record.Definition.Id);
            if (saved == null)
            {
                await CreateProfile(record.ProfileId, owner, record.Definition);
                seasons.InitializeEphemeralProfile(
                    saves.GetProfile(record.ProfileId).CharacterData!.PmcData!,
                    record.ProfileId,
                    record.Definition.Id
                );
            }
            else
            {
                var profile =
                    json.Deserialize<SptProfile>(record.ProfileJson)
                    ?? throw new InvalidDataException("The saved test profile is unreadable.");
                if (profile.ProfileInfo?.ProfileId?.ToString() != record.ProfileId)
                    throw new InvalidDataException("The saved test profile identity does not match.");
                // A process restart abandons the old raid, never resumes its live world.
                if (profile.InraidData != null)
                    profile.InraidData.Location = "none";
                HubProfileStore.Add(saves, record.ProfileId, profile);
                seasons.RefreshCatalogue(record.Definition);
            }
            CampaignReconciliation.Apply(
                saves.GetProfile(record.ProfileId).CharacterData!.PmcData!,
                record.Definition,
                json,
                record.Definition
            );
            seasons.ReconcileEffectParameters(saves.GetProfile(record.ProfileId).CharacterData!.PmcData!, record.Definition);
            await story.NewSession(record.ProfileId);
            Save(state);
            state.Saving = true;
            SaveHandlers[record.ProfileId] = () =>
            {
                if (state.Saving)
                    Save(state);
            };
            _ended.TryRemove(record.ProfileId, out _);
            _tests[record.ProfileId] = state;
            return Projection(state);
        }
        catch
        {
            Release(state);
            throw;
        }
    }

    public Task<CampaignTestResponse> Status(string identity, CampaignTestRequest request)
    {
        CampaignTestPolicy.RequireRequest(request, CampaignTestActions.Status);
        if (_ended.TryGetValue(request.TestId, out var ended))
        {
            if (Owner(identity) != ended.Owner)
                throw new InvalidOperationException("This test belongs to another account.");
            return Task.FromResult(ended.Response);
        }
        return Task.FromResult(Projection(Require(identity, request)));
    }

    public async Task<CampaignTestResponse> Reset(string identity, CampaignTestRequest request)
    {
        CampaignTestPolicy.RequireRequest(request, CampaignTestActions.Reset);
        var old = Require(identity, request, allowResetReplay: true);
        if (old.Record.LastOperation == request.OperationId && old.Record.LastAction == "reset")
            return Projection(old);
        CheckOperation(old, request);
        using var lease = seasons.Enter(old.Record.ProfileId);
        seasons.EnsureNotInRaid(old.Record.ProfileId);
        var draft = LoadDraft(old.Record.DraftId);
        CampaignTestPolicy.RequireUpdate(request, old.Record.SourceRevision, draft.Revision, false);
        var record = NewRecord(old.Record.Owner, draft);
        record.LastOperation = request.OperationId;
        record.LastAction = "reset";
        record.RetiredProfiles = new(old.Record.RetiredProfiles) { old.Record.ProfileId };
        var replacement = new TestState
        {
            Record = record,
            EditorSessionId = old.EditorSessionId,
            ReturnProfileId = old.ReturnProfileId,
        };
        TestIds[record.ProfileId] = 0;
        try
        {
            Register(replacement);
            seasons.RegisterEphemeral(record.ProfileId, record.Definition.Id);
            await CreateProfile(record.ProfileId, record.Owner, record.Definition);
            seasons.InitializeEphemeralProfile(
                saves.GetProfile(record.ProfileId).CharacterData!.PmcData!,
                record.ProfileId,
                record.Definition.Id
            );
            CampaignReconciliation.Apply(
                saves.GetProfile(record.ProfileId).CharacterData!.PmcData!,
                record.Definition,
                json,
                record.Definition
            );
            seasons.ReconcileEffectParameters(saves.GetProfile(record.ProfileId).CharacterData!.PmcData!, record.Definition);
            await story.NewSession(record.ProfileId);
            old.Saving = false;
            try
            {
                Save(replacement);
            }
            catch
            {
                old.Saving = true;
                throw;
            }
            Release(old);
            _tests.TryRemove(old.Record.ProfileId, out _);
            replacement.Saving = true;
            SaveHandlers[record.ProfileId] = () =>
            {
                if (replacement.Saving)
                    Save(replacement);
            };
            _tests[record.ProfileId] = replacement;
            return Projection(replacement);
        }
        catch
        {
            Release(replacement);
            throw;
        }
    }

    public Task<CampaignTestResponse> Apply(string identity, CampaignTestRequest request)
    {
        CampaignTestPolicy.RequireRequest(request, CampaignTestActions.Apply);
        var state = Require(identity, request);
        if (state.Record.LastOperation == request.OperationId && state.Record.LastAction == "apply" && request.OperationId.Length > 0)
            return Task.FromResult(Projection(state));
        CheckOperation(state, request);
        using var lease = seasons.Enter(state.Record.ProfileId);
        seasons.EnsureNotInRaid(state.Record.ProfileId);
        var draft = LoadDraft(state.Record.DraftId);
        CampaignTestPolicy.RequireUpdate(request, state.Record.SourceRevision, draft.Revision, false);
        var old = state.Record;
        var next = SeasonCompiler.Copy(old);
        next.Definition = SeasonRepository.Duplicate(draft.Definition, next.Identities);
        next.Definition.Revision = draft.Revision;
        PreserveInventoryTemplates(old.Definition, next.Definition);
        next.SourceRevision = draft.Revision;
        next.LastOperation = request.OperationId;
        next.LastAction = "apply";
        content.RequireTestContentReady(next.Definition);
        var original = saves.GetProfile(old.ProfileId);
        var staged = json.Deserialize<SptProfile>(json.Serialize(original)!)!;
        CampaignReconciliation.Apply(staged.CharacterData!.PmcData!, next.Definition, json, old.Definition);
        state.Saving = false;
        try
        {
            CampaignTestUpdateTransaction.Commit(
                () =>
                {
                    DisposeContent(state);
                    state.Record = next;
                    Register(state);
                    seasons.ReconcileEffectParameters(staged.CharacterData!.PmcData!, next.Definition);
                    next.ProfileJson = json.Serialize(staged)!;
                    HubProfileStore.Replace(saves, old.ProfileId, original, staged);
                },
                () => _storage.Save(next),
                () =>
                {
                    if (ReferenceEquals(saves.GetProfile(old.ProfileId), staged))
                        HubProfileStore.Replace(saves, old.ProfileId, staged, original);
                    DisposeContent(state);
                    state.Record = old;
                    Register(state);
                }
            );
            seasons.RefreshCatalogue(next.Definition);
            state.Saving = true;
            return Task.FromResult(Projection(state));
        }
        catch
        {
            state.Saving = repository.IsIsolatedSnapshot(old.Definition.Id);
            if (!state.Saving)
            {
                Release(state);
                _tests.TryRemove(old.ProfileId, out _);
            }
            throw;
        }
    }

    public Task<CampaignTestResponse> End(string identity, CampaignTestRequest request)
    {
        CampaignTestPolicy.RequireRequest(request, CampaignTestActions.End);
        if (_ended.TryGetValue(request.TestId, out var ended))
        {
            if (Owner(identity) != ended.Owner)
                throw new InvalidOperationException("This test belongs to another account.");
            return Task.FromResult(ended.Response);
        }
        var state = Require(identity, request);
        using var lease = seasons.Enter(state.Record.ProfileId);
        seasons.EnsureNotInRaid(state.Record.ProfileId);
        Save(state);
        var response = Projection(state);
        response.Status = "Ended";
        response.Message = "Test progress saved.";
        Release(state);
        _tests.TryRemove(state.Record.ProfileId, out _);
        _ended[state.Record.ProfileId] = (state.Record.Owner, response);
        return Task.FromResult(response);
    }

    // Persisted tests can stay suspended indefinitely. There is no heartbeat expiry
    // that discards progress or removes native content from a live raid.
    public Task RecoverAbandoned(string identity) => Task.CompletedTask;

    private CampaignTestRecord NewRecord(string owner, DraftEnvelope draft)
    {
        var record = new CampaignTestRecord
        {
            Owner = owner,
            DraftId = draft.Id,
            ProfileId = SeasonRepository.NewId(),
            SourceRevision = draft.Revision,
        };
        record.Definition = SeasonRepository.Duplicate(draft.Definition, record.Identities);
        record.Definition.Revision = draft.Revision;
        return record;
    }

    private void Register(TestState state)
    {
        var definition = state.Record.Definition;
        content.RequireTestContentReady(definition);
        try
        {
            state.Registrations.Add(repository.RegisterIsolatedSnapshot(definition));
            state.Registrations.Add(content.RegisterIsolated(definition));
            state.Registrations.Add(hubQuests.RegisterIsolated(definition));
            state.Registrations.Add(hub.RegisterIsolated(repository.Runtime(definition.Id)));
            foreach (var asset in SeasonCompiler.Assets(definition).Where(SeasonValidator.IsId))
                images.AddRoute("/wtt-campaigns/hub-images/" + asset, repository.AssetPath(asset)!);
            foreach (var perk in definition.Perks.All)
                if (repository.AssetPath(perk.ImageUrl) is { } path)
                    images.AddRoute("/wtt-campaigns/icons/" + perk.Id, path);
        }
        catch
        {
            DisposeContent(state);
            throw;
        }
    }

    private static void DisposeContent(TestState state)
    {
        foreach (var registration in state.Registrations.AsEnumerable().Reverse())
            registration.Dispose();
        state.Registrations.Clear();
    }

    private void Release(TestState state)
    {
        state.Saving = false;
        SaveHandlers.TryRemove(state.Record.ProfileId, out _);
        seasons.UnregisterEphemeral(state.Record.ProfileId);
        HubProfileStore.Remove(saves, state.Record.ProfileId);
        DisposeContent(state);
    }

    private void Save(TestState state)
    {
        state.Record.ProfileJson = json.Serialize(saves.GetProfile(state.Record.ProfileId))!;
        _storage.Save(state.Record);
    }

    private CampaignTestResponse Projection(TestState state)
    {
        var record = state.Record;
        var latest = record.SourceRevision;
        try
        {
            latest = repository.Load(record.DraftId).Revision;
        }
        catch (Exception e) when (e is IOException or Newtonsoft.Json.JsonException or InvalidDataException) { }
        return new CampaignTestResponse
        {
            EditorSessionId = state.EditorSessionId,
            DraftId = record.DraftId,
            TestId = record.ProfileId,
            ProfileId = record.ProfileId,
            SeasonId = record.Definition.Id,
            ReturnProfileId = state.ReturnProfileId,
            LoadedDraftRevision = record.SourceRevision,
            LatestDraftRevision = latest,
            Status = "Ready",
            Message =
                $"CAMPAIGN TEST · {record.Definition.Name} · Loaded r{record.SourceRevision}"
                + (latest != record.SourceRevision ? $" · Saved r{latest} available" : " · Up to date"),
            Committed = true,
        };
    }

    private string Owner(string identity)
    {
        if (!SeasonValidator.IsId(identity) || IsTest(identity) || !saves.ProfileExists(identity))
            throw new InvalidOperationException("Use your launcher account to control campaign tests.");
        return saves.GetProfile(identity).CharacterData?.PmcData == null ? identity : seasons.ResolveRoot(identity);
    }

    private TestState Require(string identity, CampaignTestRequest request, bool allowResetReplay = false)
    {
        if (!_tests.TryGetValue(request.TestId, out var state))
        {
            state = allowResetReplay
                ? _tests.Values.FirstOrDefault(s =>
                    s.Record.RetiredProfiles.Contains(request.TestId) && s.Record.LastOperation == request.OperationId
                )
                : null;
            if (state == null)
                throw new InvalidOperationException(
                    "This test session is no longer active. Continue the saved test from the campaign menu."
                );
        }
        if (identity != state.Record.Owner && identity != state.ReturnProfileId && !EditorIdentityMatches(state.EditorSessionId, identity))
            throw new InvalidOperationException("This test belongs to another account.");
        if (state.Record.DraftId != request.DraftId || state.EditorSessionId != request.EditorSessionId)
            throw new InvalidOperationException("The test draft or editor session changed.");
        return state;
    }

    private static void CheckOperation(TestState state, CampaignTestRequest request)
    {
        if (!Guid.TryParseExact(request.OperationId, "N", out _) || request.ExpectedLoadedRevision != state.Record.SourceRevision)
            throw new InvalidOperationException("The test changed. Refresh its status before applying changes.");
    }

    private DraftEnvelope LoadDraft(string id)
    {
        var draft = repository.Load(id);
        if (draft.Status != DraftStatus.Active || draft.Definition.MissionPackage != null || draft.Definition.Legacy)
            throw new InvalidOperationException("Choose an active campaign draft to test.");
        return draft;
    }

    private static void PreserveInventoryTemplates(SeasonDefinition old, SeasonDefinition next)
    {
        foreach (var item in old.Items.Where(i => next.Items.All(n => n.Id != i.Id)))
            next.Items.Add(item);
        foreach (var item in old.ImportedItems)
            next.ImportedItems.TryAdd(item.Key, item.Value);
    }
}

public sealed class CampaignTestRouteRequest : CampaignTestRequest, SPTarkov.Server.Core.Models.Utils.IRequestData;
