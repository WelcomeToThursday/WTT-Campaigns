using SPTarkov.Server.Core.Models.Eft.Common;
using WTT.Campaigns.Shared.Story;

namespace WTT.Campaigns.Server.Story;

public sealed partial class StoryService
{
    private List<StoryObjective> Objectives(PmcData pmc, StoryDefinition definition, StoryProgress state, StoryFacts facts)
    {
        var output = new List<StoryObjective>();
        var pack = repository.Runtime(state.SeasonId).Definition;
        foreach (var metadata in definition.Quests)
        {
            var template = pack.Quests.FirstOrDefault(q => (string?)q.Id == metadata.QuestId);
            if (template == null)
            {
                continue;
            }
            var status = facts.QuestStatuses.GetValueOrDefault(metadata.QuestId) ?? "Locked";
            var available =
                status is not ("Locked" or "AvailableAfter")
                && !metadata.Hidden
                && definition.Chapters.Any(c => c.Id == metadata.ChapterId && StoryRules.Evaluate(c.Visibility, definition, state, facts))
                && StoryRules.Evaluate(metadata.Visibility, definition, state, facts);
            foreach (var condition in template.Conditions.AvailableForFinish)
            {
                var id = (string)condition.Id!;
                var complete = status == "Success" || Condition(condition, pmc, definition, state, facts);
                var required = (double?)condition.Value ?? 1;
                var visible = available;
                if (condition.VisibilityConditions is { } visibility)
                {
                    visible &= visibility.All(v => facts.CompletedConditions.Contains((string?)v.Target ?? (string?)v.ConditionId ?? ""));
                }
                var text =
                    template.Localization.GetValueOrDefault("en")?.GetValueOrDefault(id)
                    ?? pack.Locales.GetValueOrDefault("en")?.GetValueOrDefault(id)
                    ?? id;
                output.Add(
                    new StoryObjective
                    {
                        Id = id,
                        QuestId = metadata.QuestId,
                        ChapterId = metadata.ChapterId,
                        Text = text,
                        Hint = (string?)condition.Hint ?? "",
                        Required = required,
                        Current = complete ? required : facts.ConditionCounters.GetValueOrDefault(id),
                        Main = metadata.Main && (bool?)condition.IsNecessary != false,
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
