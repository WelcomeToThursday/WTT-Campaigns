using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WTT.Campaigns.Server.Web.Authoring;
using WTT.Campaigns.Shared.Seasons;
using WTT.Campaigns.Shared.Story;

namespace WTT.Campaigns.Tests;

internal static class EditorLayoutChecks
{
    public static void Run(Action<bool, string> check)
    {
        var condition = NativeQuestAuthoring.Condition("HandoverItem");
        condition.Value = 3;
        var before = JsonConvert.SerializeObject(condition);
        check(EditorFieldGuide.NativeHelp(condition, "value").Contains("consumed"), "Handover help explains item consumption");
        check(EditorFieldGuide.NativeHelp(condition, "value").Contains("3"), "Handover help reflects its edited quantity");
        condition.ConditionType = "FindItem";
        check(
            !EditorFieldGuide.NativeHelp(condition, "value").Contains("consumed"),
            "Changing condition type removes handover-specific guidance"
        );
        condition.OnlyFoundInRaid = true;
        check(
            EditorFieldGuide.NativeHelp(condition, "onlyFoundInRaid").StartsWith("Only found-in-raid"),
            "Quality help follows the raid-origin toggle"
        );
        condition.MinDurability = 30;
        condition.MaxDurability = 80;
        check(
            EditorFieldGuide.NativeHelp(condition, "minDurability").Contains("30% to 80%"),
            "Durability help includes both current bounds"
        );
        var counter = NativeQuestAuthoring.Condition("CounterCreator");
        counter.OneSessionOnly = true;
        check(EditorFieldGuide.NativeHelp(counter, "value").Contains("one raid"), "Counter quantity help follows single-raid requirement");
        var kills = NativeQuestAuthoring.Condition("Kills");
        var distance = kills.Distance!;
        distance.Value = 75;
        check(EditorFieldGuide.NativeKind(distance) == "Kills", "Nested restrictions inherit the enclosing event type");
        check(
            EditorFieldGuide.NativeHelp(distance, "value").Contains("75 metres"),
            "Nested distance uses metres rather than objective count"
        );
        var filter = new WTT.Campaigns.Shared.Effects.ItemFilterRule { Field = "_tpl", Value = "abc" };
        check(EditorFieldGuide.NativeHelp(filter, "value").Contains("individual item"), "Item filter explains an item target");
        filter.Field = "ParentId";
        check(EditorFieldGuide.NativeHelp(filter, "value").Contains("category"), "Filter help follows item-to-category changes");
        var effect = new WTT.Campaigns.Shared.Effects.PerkEffect { EffectId = "energy_drain_multiplicator", Multiplier = 0.8 };
        check(
            EditorFieldGuide.NativeHelp(effect, "multiplicator").Contains("energy last longer"),
            "Energy effect help explains why a lower multiplier helps"
        );
        effect.EffectId = "stamina_restore_body_parts_multiplicator";
        check(
            EditorFieldGuide.NativeHelp(effect, "multiplicator").Contains("Higher values restore"),
            "Recovery effect help reverses the beneficial direction"
        );

        var season = new SeasonDefinition { Story = new StoryDefinition() };
        season.Story.Variables.Add(
            new StoryVariable
            {
                Id = "phase",
                InitialValue = 7,
                Scope = StoryVariableScope.Profile,
            }
        );
        var test = new StoryCondition
        {
            Type = "VariableValue",
            Target = "phase",
            Value = 9,
            Operator = "==",
        };
        var help = EditorFieldGuide.StoryHelp(test, "Value", season)!;
        check(
            help.Contains("== 9") && help.Contains("Profile scope, initial value 7"),
            "Variable help shows current comparison and selected variable declaration"
        );
        season.Story.Variables[0].Scope = StoryVariableScope.Session;
        check(EditorFieldGuide.StoryHelp(test, "Target", season)!.Contains("resets on reconnect"), "Variable help follows scope edits");
        test.Type = "CompleteCondition";
        check(
            EditorFieldGuide.StoryHelp(test, "Target", season)!.Contains("not partial progress"),
            "Objective state explains boolean completion instead of numeric progress"
        );
        test.Type = "TraderReputation";
        check(
            EditorFieldGuide.StoryLabel(test, "Value") == "Reputation threshold"
                && EditorFieldGuide.StoryHelp(test, "Value")!.Contains("decimal"),
            "Reputation condition uses decimal-specific units and label"
        );

        var quest = NativeQuestAuthoring.Create();
        var conditionsBefore = JsonConvert.SerializeObject(quest.Conditions);
        NativeQuestAuthoring.AddQuestReward(quest, "Experience", "Started");
        NativeQuestAuthoring.AddQuestReward(quest, "TraderStanding", "Fail");
        NativeQuestAuthoring.AddQuestReward(quest, "Item");
        check((string?)quest.Rewards["Started"][0].Type == "Experience", "Acceptance reward is stored in Started");
        check((string?)quest.Rewards["Fail"][0].Type == "TraderStanding", "Failure reward is stored in Fail");
        check((string?)quest.Rewards["Success"][0].Type == "Item", "Default reward creation remains in Success");
        check(JsonConvert.SerializeObject(quest.Conditions) == conditionsBefore, "Reward-stage editing preserves objective definitions");
        condition = JsonConvert.DeserializeObject<NativeCondition>(before)!;
        foreach (var property in ModelGraph.Properties(condition))
        {
            _ = EditorFieldGuide.NativeGroup(ModelGraph.Name(property));
            _ = EditorFieldGuide.NativeLabel(condition, property.Name);
            _ = EditorFieldGuide.NativeHelp(condition, property.Name);
        }
        check(JsonConvert.SerializeObject(condition) == before, "Generating groups and guidance leaves authoring data unchanged");
    }
}
