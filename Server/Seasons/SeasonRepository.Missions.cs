using WTT.Campaigns.Shared.Missions;
using WTT.Campaigns.Shared.Seasons;

namespace WTT.Campaigns.Server.Seasons;

public sealed partial class SeasonRepository
{
    public System.Collections.Concurrent.ConcurrentDictionary<string, SeasonRuntimeSnapshot> PublishedMissions { get; } = new();

    public List<(string Key, SeasonDefinition Definition)> MissionPacks()
    {
        var output = new List<(string Key, SeasonDefinition Definition)>();
        foreach (var pack in Packs().Where(p => p.Manifest.ContentKind == "Mission"))
        {
            try
            {
                output.Add((pack.Key, Pack(pack.Key)));
            }
            catch (Exception error) when (error is IOException or InvalidOperationException or Newtonsoft.Json.JsonException)
            {
                var message = "Mission pack " + pack.Key + ": " + error.Message;
                if (!StorageWarnings.Contains(message))
                    StorageWarnings.Add(message);
            }
        }
        return output;
    }

    public DraftEnvelope CreateMission() => CreateMissionDraft("New mission", "");

    public DraftEnvelope CreateMission(string name, string location)
    {
        name = (name ?? "").Trim();
        location = (location ?? "").Trim();
        if (name.Length is < 1 or > 120 || location.Length is < 1 or > 120 || location == "hideout")
            throw new InvalidOperationException("Enter a mission name (up to 120 characters) and choose a raid location.");
        return CreateMissionDraft(name, location);
    }

    private DraftEnvelope CreateMissionDraft(string name, string location)
    {
        var layout = new WTT.Campaigns.Shared.Spatial.MapLayout
        {
            Id = NewId(),
            Name = location.Length == 0 ? "Mission layout" : name,
            Location = location,
        };
        var mission = new MissionDefinition
        {
            Id = NewId(),
            Name = name,
            Briefing = "Complete the route and extract.",
            LayoutId = layout.Id,
            CheckpointRetries = true,
        };
        return Save(
            new DraftEnvelope
            {
                Id = NewId(),
                Definition = new SeasonDefinition
                {
                    Id = mission.Id,
                    BattlePassId = NewId(),
                    Name = mission.Name,
                    FormatVersion = Math.Max(
                        MissionLibrary.FormatVersion,
                        WTT.Campaigns.Shared.Spatial.MapLayoutRules.Format(new[] { layout })
                    ),
                    MissionPackage = new(),
                    Missions = [mission],
                    MapLayouts = [layout],
                },
            }
        );
    }

    public DraftEnvelope ExtractMission(DraftEnvelope campaign, string missionId)
    {
        // Extraction only copies. The source is retained until a published package is explicitly linked.
        var mission = SeasonCompiler.Copy(campaign.Definition.Missions.Single(m => m.Id == missionId));
        var layout = SeasonCompiler.Copy(campaign.Definition.MapLayouts.Single(l => l.Id == mission.LayoutId));
        mission.QuestId = "";
        mission.CompletionConditionId = "";
        layout.ApplyInNormalRaids = false;
        var package = new SeasonDefinition
        {
            Id = mission.Id,
            BattlePassId = NewId(),
            Name = mission.Name,
            Author = campaign.Definition.Author,
            FormatVersion = Math.Max(MissionLibrary.FormatVersion, WTT.Campaigns.Shared.Spatial.MapLayoutRules.Format(new[] { layout })),
            MissionPackage = new(),
            Missions = [mission],
            MapLayouts = [layout],
            Zones = SeasonCompiler.Copy(
                campaign
                    .Definition.Zones.Where(z => z.LayoutId == layout.Id || z.LayoutId.Length == 0 && z.Location == layout.Location)
                    .ToList()
            ),
            Dependencies = campaign.Definition.Dependencies.Where(d => d.StartsWith("mod:", StringComparison.Ordinal)).ToList(),
            Items = SeasonCompiler.Copy(campaign.Definition.Items),
            ImportedItems = SeasonCompiler.Copy(campaign.Definition.ImportedItems),
        };
        foreach (var zone in package.Zones)
            zone.LayoutId = layout.Id;
        var required = MissionLibrary.ReferencedItems(package).ToHashSet();
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var item in package.Items.Where(i => required.Contains(i.Id)))
                changed |= required.Add(item.CloneFrom);
            foreach (var item in package.ImportedItems.Where(i => required.Contains(i.Key)))
            foreach (var text in WTT.Campaigns.Shared.Serialization.ModelGraph.Texts(item.Value))
                if (package.ImportedItems.ContainsKey(text.Value) || package.Items.Any(i => i.Id == text.Value))
                    changed |= required.Add(text.Value);
        }
        package.Items.RemoveAll(i => !required.Contains(i.Id));
        package.ImportedItems = package.ImportedItems.Where(i => required.Contains(i.Key)).ToDictionary(i => i.Key, i => i.Value);
        // Give copied custom items their own identities so the reusable package can
        // coexist with its source campaign's templates in the native database.
        var itemIds = package.Items.Select(i => i.Id).Concat(package.ImportedItems.Keys).Distinct().ToDictionary(id => id, _ => NewId());
        WTT.Campaigns.Shared.Serialization.ModelGraph.Rewrite(package, text => itemIds.GetValueOrDefault(text) ?? text);
        return Save(new DraftEnvelope { Id = NewId(), Definition = package });
    }

    public CampaignMissionLink LinkMission(SeasonDefinition campaign, string key, string legacyMissionId = "")
    {
        var package = Pack(key);
        var validation = SeasonValidator.Validate(package);
        if (package.MissionPackage == null || !validation.CanPublish)
            throw new InvalidOperationException("Choose a valid published mission revision.");
        var mission = package.Missions.Single();
        if (campaign.MissionLinks.Any(l => l.MissionId == mission.Id))
            throw new InvalidOperationException("This mission is already linked to the campaign.");
        var legacy = legacyMissionId.Length == 0 ? null : campaign.Missions.Single(m => m.Id == legacyMissionId);
        if (legacy != null && mission.Id != legacy.Id)
            throw new InvalidOperationException("Choose the package extracted from this mission.");
        var link = new CampaignMissionLink
        {
            Id = legacy?.Id ?? NewId(),
            MissionId = mission.Id,
            Revision = package.Revision,
            ContentHash = MissionLibrary.PackageHash(package),
            Package = package,
            Availability = legacy == null ? MissionAvailability.FromStart : MissionAvailability.QuestAccepted,
            UnlockTargetId = legacy?.QuestId ?? "",
            QuestId = legacy?.QuestId ?? "",
            CompletionConditionId = legacy?.CompletionConditionId ?? "",
            LegacyMissionId = legacy?.Id ?? "",
        };
        var proposed = SeasonCompiler.Copy(campaign);
        proposed.FormatVersion = Math.Max(proposed.FormatVersion, MissionLibrary.FormatVersion);
        if (legacy != null)
            proposed.Missions.RemoveAll(m => m.Id == legacy.Id);
        proposed.MissionLinks.Add(link);
        var links = new SeasonValidationResult();
        MissionLibrary.ValidateLinks(proposed, links);
        if (!links.CanPublish)
            throw new InvalidOperationException(string.Join("; ", links.Issues.Select(i => i.Message)));
        campaign.FormatVersion = proposed.FormatVersion;
        if (legacy != null)
            campaign.Missions.RemoveAll(m => m.Id == legacy.Id);
        campaign.MissionLinks.Add(link);
        return link;
    }

    public void UpdateMissionLink(CampaignMissionLink link, string key)
    {
        var package = Pack(key);
        if (
            package.MissionPackage == null
            || package.Missions.SingleOrDefault()?.Id != link.MissionId
            || !SeasonValidator.Validate(package).CanPublish
        )
            throw new InvalidOperationException("Choose a valid revision of the linked mission.");
        link.Package = package;
        link.Revision = package.Revision;
        link.ContentHash = MissionLibrary.PackageHash(package);
    }
}
