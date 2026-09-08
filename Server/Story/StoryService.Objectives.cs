using Newtonsoft.Json.Linq;
using SeasonalPerks.Shared.Story;
using SPTarkov.Server.Core.Models.Eft.Common;

namespace SeasonalPerks.Server.Story;

public sealed partial class StoryService
{
    private List<StoryObjective> Objectives(PmcData pmc, StoryDefinition definition, StoryProgress state, StoryFacts facts)
    {
        var output = new List<StoryObjective>();
        var pack = repository.Runtime(state.SeasonId).Definition;
        foreach (var metadata in definition.Quests)
        {
            var template = pack.Quests.OfType<JObject>().FirstOrDefault(q => (string?)q["_id"] == metadata.QuestId);
            if (template == null)
            {
                continue;
            }
            var status = facts.QuestStatuses.GetValueOrDefault(metadata.QuestId) ?? "Locked";
            var available =
                status is not ("Locked" or "AvailableAfter")
                && !metadata.Hidden
                && StoryRules.Evaluate(metadata.Visibility, definition, state, facts);
            foreach (var condition in ((JArray)template["conditions"]!["AvailableForFinish"]!).OfType<JObject>())
            {
                var id = (string)condition["id"]!;
                var complete = status == "Success" || Condition(condition, pmc, definition, state, facts);
                var required = (double?)condition["value"] ?? 1;
                var visible = available;
                if (condition["visibilityConditions"] is JArray visibility)
                {
                    visible &= visibility
                        .OfType<JObject>()
                        .All(v => facts.CompletedConditions.Contains((string?)v["target"] ?? (string?)v["conditionId"] ?? ""));
                }
                var text =
                    (string?)template["localization"]?["en"]?[id] ?? pack.Locales.GetValueOrDefault("en")?.GetValueOrDefault(id) ?? id;
                output.Add(
                    new StoryObjective
                    {
                        Id = id,
                        QuestId = metadata.QuestId,
                        ChapterId = metadata.ChapterId,
                        Text = text,
                        Hint = (string?)condition["hint"] ?? "",
                        Required = required,
                        Current = complete ? required : facts.ConditionCounters.GetValueOrDefault(id),
                        Main = metadata.Main && (bool?)condition["isNecessary"] != false,
                        Complete = complete,
                        Failed = status is "Fail" or "FailRestartable" or "MarkedAsFailed" or "Expired",
                        Visible = visible,
                    }
                );
            }
        }
        return output;
    }
}
