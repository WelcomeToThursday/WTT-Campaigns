using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WTT.Campaigns.Server.Seasons;
using WTT.Campaigns.Server.Web.Authoring;
using WTT.Campaigns.Shared.Seasons;
using WTT.Campaigns.Shared.Spatial;
using WTT.Campaigns.Shared.Story;

namespace WTT.Campaigns.Tests;

internal static class SalvageChecks
{
    public static void Run(Action<bool, string> check)
    {
        var season = new SeasonDefinition { FormatVersion = 2 };
        var zone = new SeasonZone
        {
            Id = SeasonRepository.NewId(),
            Location = "woods",
            Scene = "woods_main",
            Uses = ["Salvage"],
        };
        season.Zones.Add(zone);
        check(SpatialRules.Errors(season, false).Count == 0, "Incomplete salvage captures can be saved for browser configuration");
        check(SpatialRules.Errors(season).Count > 0, "Incomplete salvage captures cannot publish");
        zone.Salvage.RequiredItemTpl = "54491c4f4bdc2db1078b4568";
        zone.Salvage.SalvageTime = 7.5f;
        zone.Salvage.ConsumeRequiredItem = false;
        zone.Salvage.Rewards.Add(
            new()
            {
                ItemTpl = "5449016a4bdc2d6f028b456f",
                Count = 3,
                ToQuestInventory = true,
            }
        );
        var quest = NativeQuestAuthoring.Create();
        var condition = NativeQuestAuthoring.Condition("Salvage");
        SpatialRules.Assign(condition, zone.Id);
        quest.Conditions.AvailableForFinish.Add(condition);
        season.Quests.Add(quest);
        check(SpatialRules.Errors(season).Count == 0, "Configured salvage zones satisfy spatial contracts");
        check(
            NativeQuestAuthoring.ConditionKinds(true, false).Contains("Salvage")
                && NativeQuestAuthoring.ConditionKinds(true, true).Contains("Salvage")
                && StoryQuestCompatibility.Supports(condition)
                && StoryQuestCompatibility.Supports(condition, true),
            "Salvage supports standalone and nested native story objectives"
        );
        var json = JObject.FromObject(condition);
        check(
            (string?)json["conditionType"] == "Salvage"
                && (string?)json["zoneId"] == zone.Id
                && json["target"] == null
                && (double?)json["value"] == 1,
            "Salvage emits the CommonLib condition and zone contract"
        );
        var reloaded = JsonConvert.DeserializeObject<SeasonDefinition>(JsonConvert.SerializeObject(season))!;
        check(
            reloaded.Zones[0].Salvage.SalvageTime == 7.5f
                && !reloaded.Zones[0].Salvage.ConsumeRequiredItem
                && reloaded.Zones[0].Salvage.Rewards.Single().Count == 3
                && reloaded.Zones[0].Salvage.Rewards.Single().ToQuestInventory,
            "Salvage settings survive pack serialization"
        );
        var duplicate = SeasonRepository.Duplicate(season);
        check(
            duplicate.Zones[0].Id != zone.Id
                && SpatialRules.Conditions(duplicate).Any(c => c.ConditionType == "Salvage" && c.ZoneId == duplicate.Zones[0].Id),
            "Duplicated salvage objectives refer to the duplicated zone"
        );
        check(
            SeasonCompiler.Dependencies(season).Contains("item:" + zone.Salvage.RequiredItemTpl)
                && SeasonCompiler.Dependencies(season).Contains("item:" + zone.Salvage.Rewards[0].ItemTpl),
            "Pack dependencies include salvage tools and rewards"
        );
        zone.Salvage.Rewards[0].Count = 0;
        check(SpatialRules.Errors(season).Count > 0, "Zero salvage reward counts are rejected");
        zone.Salvage.Rewards[0].Count = 1;
        zone.Salvage.SalvageTime = float.NaN;
        check(SpatialRules.Errors(season).Count > 0, "Non-finite salvage duration is rejected");
        zone.Salvage.SalvageTime = 10;
        zone.Uses.Add("LeaveItemAtLocation");
        check(SpatialRules.Errors(season).Count > 0, "Placement cannot hide the salvage interaction on the same zone");
    }
}
