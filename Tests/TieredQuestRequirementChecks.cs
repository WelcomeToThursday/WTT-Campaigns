using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Utils.Json;
using WTT.Campaigns.Server.Progression;
using WTT.Campaigns.Shared.Progression;

namespace WTT.Campaigns.Tests;

internal static class TieredQuestRequirementChecks
{
    internal static void Run(Action<bool, string> check)
    {
        const string quest = "5fd9fad9c1ce6b1a3b486d00";
        const string trader = "54cb50c76803fa8b248b4571";
        // Original release data: the only imported condition is the loyalty gate.
        var oldSpec = new TaskProgression
        {
            TraderId = trader,
            Tier = 1,
            Start =
            [
                new()
                {
                    ConditionType = "TraderLoyalty",
                    Target = trader,
                    Value = 1,
                    CompareMethod = ">=",
                },
            ],
        };
        var level = new QuestCondition
        {
            DynamicLocale = false,
            Id = new MongoId("5fd9fad9c1ce6b1a3b486d02"),
            ConditionType = "Level",
            Value = 5,
            CompareMethod = ">=",
        };
        var previous = new QuestCondition
        {
            DynamicLocale = false,
            Id = new MongoId("5fdc862eaf5a054cc9333005"),
            ConditionType = "Quest",
            Target = new ListOrT<string>(null, "5936d90786f7742b1420ba5b"),
            Status = [QuestStatusEnum.Success, QuestStatusEnum.Fail],
            AvailableAfter = 3600,
        };
        List<QuestCondition> native = [level, previous];
        var before = System.Text.Json.JsonSerializer.Serialize(native);
        QuestStartRequirements.Apply(native, quest, oldSpec);
        check(
            native.Count == 3 && ReferenceEquals(native[0], level) && ReferenceEquals(native[1], previous),
            "DLL-only hotfix retains native prerequisites with old loyalty-only data"
        );
        check(
            System.Text.Json.JsonSerializer.Serialize(native.Take(2).ToList()) == before,
            "Native levels, branch statuses, delays and other fields survive unchanged"
        );
        check(
            native[2].Id.ToString() == "6027f56bd4a9777d1bcf9b0f" && native[2].Value == 1 && native[2].Target!.Item == trader,
            "Runtime loyalty gate matches the deterministic packaged condition"
        );
        QuestStartRequirements.Apply(native, quest, oldSpec);
        check(native.Count == 3, "Applying the hotfix twice does not duplicate loyalty gates");

        List<QuestCondition> starter = [];
        QuestStartRequirements.Apply(starter, quest, oldSpec);
        check(starter.Count == 1 && starter[0].ConditionType == "TraderLoyalty", "Genuine starters remain available at their loyalty tier");
        starter[0].Value = 3;
        QuestStartRequirements.Apply(starter, quest, oldSpec);
        check(starter.Count == 1 && starter[0].Value == 3, "Stronger native loyalty requirements are preserved");

        oldSpec.Tier = 4;
        QuestStartRequirements.Apply(starter, quest, oldSpec);
        check(starter.Count == 2 && starter[0].Value == 3 && starter[1].Value == 4, "Higher task tiers still add their required gate");
        oldSpec.Tier = 0;
        List<QuestCondition> essential = [level, previous];
        QuestStartRequirements.Apply(essential, quest, oldSpec);
        check(essential.Count == 2, "Essential start rules remain untouched by tiered hotfix");
        check(oldSpec.Start.Count == 1 && oldSpec.Start[0].Value == 1, "Runtime repair never rewrites installed progression data");
    }
}
