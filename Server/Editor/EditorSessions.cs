using System.Collections.Concurrent;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services.Profile;
using WTT.Campaigns.Server.Profiles;
using WTT.Campaigns.Server.Seasons;
using WTT.Campaigns.Shared.Authoring;

namespace WTT.Campaigns.Server.Editor;

[Injectable(InjectionType.Singleton)]
public sealed class EditorSessions(
    SaveServer saves,
    CreateProfileService creator,
    TemplateTable templates,
    SeasonService seasons,
    SeasonRepository repository,
    SPTarkov.Server.Core.Utils.JsonUtil json
) : EditorSessionRegistry
{
    public void RecoverAbandoned(string identity)
    {
        if (IsScratch(identity))
            return;
        var session = Profiles.Values.FirstOrDefault(s => s.Owner == identity || s.ReturnProfile == identity);
        if (session == null)
            return;
        using var lease = seasons.Enter(session.Owner);
        if (DateTimeOffset.UtcNow - session.Contact > TimeSpan.FromMinutes(1))
            Retire(session);
    }

    public Session Require(string owner, string id)
    {
        var session = ForOwner(owner);
        if (session == null || !session.Accepts(owner, id, DateTimeOffset.UtcNow))
            throw new InvalidOperationException("Editor session expired. Return to editor home and reconnect.");
        session.Contact = DateTimeOffset.UtcNow;
        return session;
    }

    public async Task<EditorSessionResponse> Begin(string owner)
    {
        var existing = ForOwner(owner);
        if (existing != null && DateTimeOffset.UtcNow - existing.Contact < TimeSpan.FromMinutes(1))
            return Response(existing);
        if (existing != null)
            Retire(existing);
        seasons.EnsureNotInRaid(seasons.EffectiveId(owner));
        var id = Guid.NewGuid().ToString("N")[..24];
        var session = new Session
        {
            Owner = owner,
            Profile = id,
            ReturnProfile = seasons.EffectiveId(owner),
        };
        ScratchIds[id] = 0;
        Profiles[id] = session;
        Directory.CreateDirectory(ScratchDirectory);
        try
        {
            saves.CreateProfile(
                new Info
                {
                    ProfileId = new MongoId(id),
                    ScavengerId = new MongoId(Guid.NewGuid().ToString("N")[..24]),
                    Aid = saves.GetProfiles().Values.Max(p => p.ProfileInfo?.Aid ?? 0) + 1,
                    Username = "CampaignEditor",
                    Edition = saves.GetProfile(new MongoId(owner)).ProfileInfo!.Edition,
                    IsWiped = false,
                }
            );
            MongoId Cosmetic(string parent) =>
                templates
                    .Customization.Values.Where(v =>
                        v.Parent == parent && v.Properties.AvailableAsDefault && v.Properties.Side.Contains("Usec")
                    )
                    .OrderBy(v => v.Id.ToString(), StringComparer.Ordinal)
                    .First()
                    .Id;
            await creator.CreateProfile(
                new MongoId(id),
                new ProfileCreateRequestData
                {
                    Side = "Usec",
                    Nickname = "CampaignEditor",
                    HeadId = Cosmetic("5cc085e214c02e000c6bea67"),
                    VoiceId = Cosmetic("5fc100cf95572123ae738483"),
                }
            );
            var pmc = saves.GetProfile(new MongoId(id)).CharacterData!.PmcData!;
            var inventory = pmc.Inventory ?? throw new InvalidOperationException("Editor template has no inventory.");
            session.PreviewItems = Newtonsoft.Json.JsonConvert.DeserializeObject<List<WTT.Campaigns.Shared.Native.NativeItem>>(
                json.Serialize(inventory.Items)!
            )!;
            session.PreviewEquipmentId = inventory.Equipment.ToString();
            session.PreviewBindings =
                Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(json.Serialize(inventory.FastPanel)!) ?? new();
            EditorScratchInventory.Prepare(pmc);
            session.Ready = true;
            return Response(session);
        }
        catch
        {
            Retire(session);
            throw;
        }
    }

    public EditorSessionResponse Status(string owner, EditorSessionRequest request)
    {
        var session = Require(owner, request.SessionId);
        return Response(session);
    }

    public EditorSessionResponse Select(string owner, EditorSessionRequest request)
    {
        var session = Require(owner, request.SessionId);
        if (session.Location.Length > 0)
            throw new InvalidOperationException("Unload the map before changing drafts.");
        if (repository.Load(request.DraftId).Status != DraftStatus.Active)
            throw new InvalidOperationException("Restore this draft first.");
        if (request.LayoutId.Length > 0 && !repository.Load(request.DraftId).Definition.MapLayouts.Any(l => l.Id == request.LayoutId))
            throw new InvalidOperationException("Layout no longer exists in this draft.");
        var definition = repository.Load(request.DraftId).Definition;
        var layoutId =
            request.LayoutId.Length == 0 && definition.MissionPackage != null ? definition.MapLayouts.Single().Id : request.LayoutId;
        session.Select(request.DraftId, layoutId);
        return Response(session);
    }

    public EditorSessionResponse Map(string owner, EditorSessionRequest request)
    {
        var session = Require(owner, request.SessionId);
        if (session.Draft.Length == 0 || request.Location.Length is 0 or > 120)
            throw new InvalidOperationException("Select a draft and map first.");
        if (session.Location.Length > 0 && session.Location != request.Location)
            throw new InvalidOperationException("Unload the current map first.");
        if (
            session.Layout.Length > 0
            && !repository.Load(session.Draft).Definition.MapLayouts.Any(l => l.Id == session.Layout && l.Location == request.Location)
        )
            throw new InvalidOperationException("Choose the layout's map.");
        session.OpenMap(request.Location);
        return Response(session);
    }

    public EditorSessionResponse CreateLevel(string owner, EditorSessionRequest request)
    {
        var session = Require(owner, request.SessionId);
        if (session.Location.Length > 0)
            throw new InvalidOperationException("Unload the map before creating a level.");
        var draft = repository.CreateLevel(request.Name, request.Location);
        session.Select(draft.Id, draft.Definition.MapLayouts.Single().Id);
        return Response(session);
    }

    public EditorSessionResponse CreateMission(string owner, EditorSessionRequest request)
    {
        var session = Require(owner, request.SessionId);
        if (session.Location.Length > 0)
            throw new InvalidOperationException("Unload the map before creating a mission.");
        var draft = repository.CreateMission(request.Name, request.Location);
        session.Select(draft.Id, draft.Definition.MapLayouts.Single().Id);
        return Response(session);
    }

    public EditorSessionResponse Unload(string owner, EditorSessionRequest request)
    {
        var session = ForOwner(owner);
        if (session == null)
            return new EditorSessionResponse { ReturnProfileId = seasons.EffectiveId(owner) };
        if (session.Id != request.SessionId)
            throw new InvalidOperationException("Editor session does not match.");
        session.UnloadMap();
        return Response(session);
    }

    public EditorSessionResponse End(string owner, EditorSessionRequest request)
    {
        var session = ForOwner(owner);
        if (session == null)
            return new();
        if (session.Id != request.SessionId)
            throw new InvalidOperationException("Editor session does not match.");
        session.CheckCanEnd();
        var result = Response(session);
        Retire(session);
        return result;
    }

    private void Retire(Session session)
    {
        session.Ready = false;
        Profiles.TryRemove(session.Profile, out _);
        saves.GetProfiles().Remove(new MongoId(session.Profile));
        var path = Path.Combine(ScratchDirectory, session.Profile + ".json");
        if (File.Exists(path))
            File.Delete(path);
    }

    private EditorSessionResponse Response(Session s) =>
        new()
        {
            Mode = EditorContentRules.Mode(s.Draft.Length == 0 ? null : repository.Load(s.Draft).Definition, s.Layout),
            HasStory = s.Draft.Length > 0 && repository.Load(s.Draft).Definition.Story != null,
            SessionId = s.Id,
            ProfileId = s.Profile,
            ReturnProfileId = s.ReturnProfile,
            DraftId = s.Draft,
            LayoutId = s.Layout,
            Location = s.Location,
            Levels = EditorContentRules.Levels(repository.Drafts().Select(d => (d.Id, d.Definition))),
            Layouts =
                s.Draft.Length == 0
                    ? new()
                    : (repository.Drafts().FirstOrDefault(d => d.Id == s.Draft)?.Definition.MapLayouts ?? new())
                        .Select(l => new EditorLayoutChoice
                        {
                            Mode = EditorContentRules.Mode(repository.Load(s.Draft).Definition, l.Id),
                            Id = l.Id,
                            Name = l.Name,
                            Location = l.Location,
                        })
                        .ToList(),
            Drafts = repository
                .Drafts()
                .Select(d => new EditorDraftChoice
                {
                    Id = d.Id,
                    Name = d.Definition.Name,
                    Mode = EditorContentRules.Mode(d.Definition),
                    Modes = EditorContentRules.AvailableModes(d.Definition),
                })
                .ToList(),
        };
}
