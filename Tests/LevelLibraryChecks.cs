using Newtonsoft.Json;
using WTT.Campaigns.Server.Seasons;
using WTT.Campaigns.Shared.Authoring;
using WTT.Campaigns.Shared.Seasons;

namespace WTT.Campaigns.Tests;

internal static class LevelLibraryChecks
{
    internal static void Run(SeasonRepository store, Action<bool, string> check)
    {
        var packsBefore = store.Packs().Count;
        var level = store.CreateLevel(" Factory routes ", "factory4_day");
        var layout = level.Definition.MapLayouts.Single();
        check(layout.Name == "Factory routes" && layout.Location == "factory4_day", "Level creation needs only a name and map");
        check(!layout.ApplyInNormalRaids && store.Packs().Count == packsBefore, "New levels neither publish nor enable themselves");
        check(
            level.Definition.Missions.Count == 0 && level.Definition.Story == null && layout.Start == null && layout.Encounters.Count == 0,
            "New levels carry no mission, campaign story, custom start or authored AI"
        );
        var validation = SeasonValidator.Validate(level.Definition);
        check(
            validation.CanPublish,
            "Level backing storage remains compatible with existing publication: "
                + string.Join("; ", validation.Issues.Select(i => i.Message))
        );
        check(store.Load(level.Id).Definition.MapLayouts.Single().Id == layout.Id, "Direct level creation persists its identity");
        var campaign = store.Create(false);
        campaign.Definition.MapLayouts.Add(
            new()
            {
                Id = new string('a', 24),
                Name = layout.Name,
                Location = layout.Location,
                ApplyInNormalRaids = true,
            }
        );
        campaign.Definition.MapLayouts.Add(
            new()
            {
                Id = new string('b', 24),
                Name = "Legacy mission",
                Location = "woods",
                ApplyInNormalRaids = true,
            }
        );
        campaign.Definition.Missions.Add(new() { Id = new string('c', 24), LayoutId = new string('b', 24) });
        var original = JsonConvert.SerializeObject(campaign);
        var levels = EditorContentRules.Levels(new[] { (level.Id, level.Definition), (campaign.Id, campaign.Definition) });
        check(
            levels.Count == 2 && levels.All(l => l.Name == layout.Name),
            "Global level library includes old and new levels but excludes legacy missions"
        );
        check(levels.Select(l => l.DraftId + "/" + l.Id).Distinct().Count() == 2, "Same-named levels retain separate source identities");
        check(JsonConvert.SerializeObject(campaign) == original, "Library discovery preserves campaign content and enablement");
        var linked = SeasonCompiler.Copy(campaign.Definition);
        linked.MissionLinks.Add(new() { Package = new SeasonDefinition { MapLayouts = [linked.MapLayouts[1]] } });
        linked.Missions.Clear();
        check(
            EditorContentRules.Levels(new[] { (campaign.Id, linked) }).Count == 1,
            "Retained campaign layouts owned by an extracted mission link stay out of Levels"
        );
        check(
            EditorContentRules.Mode(linked, linked.MapLayouts[1].Id) == EditorContentMode.Mission,
            "Extracting a campaign mission cannot downgrade its old layout to a level"
        );
        check(
            WTT.Campaigns.Shared.Spatial.MapLayerRules.ForCharacter(new[] { linked }, "", "woods") == null,
            "Retained mission layouts cannot activate in regular raids even when their old enablement flag is set"
        );
        var missionPackage = SeasonCompiler.Copy(level.Definition);
        missionPackage.MissionPackage = new();
        check(
            EditorContentRules.Levels(new[] { ("mission", missionPackage) }).Count == 0,
            "Independent and campaign-linked mission packages are never offered as levels"
        );
        var request = JsonConvert.DeserializeObject<EditorSessionRequest>(
            JsonConvert.SerializeObject(new EditorSessionRequest { Name = "Factory routes", Location = "factory4_day" })
        )!;
        check(
            request.Name == "Factory routes" && request.Version == 2,
            "Level creation fields are additive without changing protocol gates"
        );
        var beforeInvalid = store.Drafts().Count;
        foreach (var invalid in new[] { ("", "woods"), ("Level", ""), ("Level", "hideout") })
        {
            try
            {
                store.CreateLevel(invalid.Item1, invalid.Item2);
                check(false, "Invalid level must be rejected");
            }
            catch (InvalidOperationException)
            {
                check(store.Drafts().Count == beforeInvalid, "Invalid level creation writes no draft");
            }
        }
    }
}
