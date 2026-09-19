using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using WTT.Campaigns.Server.Story;
using WTT.Campaigns.Shared.Missions;
using WTT.Campaigns.Shared.Seasons;
using WTT.Campaigns.Shared.Story;

namespace WTT.Campaigns.Server.Missions;

internal static class MissionCompletion
{
    internal static void Apply(PmcData pmc, SeasonDefinition definition, MissionDefinition mission)
    {
        var seasonId = definition.Id;
        var storyDefinition = definition.Story ?? throw new InvalidOperationException("Mission completion requires a story-backed quest.");
        if (!storyDefinition.Quests.Any(q => q.QuestId == mission.QuestId))
            throw new InvalidOperationException("The mission quest is not registered in the campaign story.");

        var questTemplate =
            definition.Quests.SingleOrDefault(q => (string?)q.Id == mission.QuestId)
            ?? throw new InvalidOperationException("The mission quest definition is unavailable.");
        var condition =
            questTemplate.Conditions.AvailableForFinish.SingleOrDefault(c => (string?)c.Id == mission.CompletionConditionId)
            ?? throw new InvalidOperationException("The mission completion objective is unavailable.");
        if (condition.ConditionType != "GlobalVariableValue" || condition.Target?.Values is not { Count: 1 } targets)
            throw new InvalidOperationException("The mission completion objective is not a profile variable objective.");

        var variableId = targets[0];
        var variable = storyDefinition.Variables.SingleOrDefault(v => v.Id == variableId);
        if (variable == null || variable.Scope != StoryVariableScope.Profile || variable.InitialValue != 0)
            throw new InvalidOperationException("The mission completion target must be a zero-initialized profile variable.");
        if (!MongoId.IsValidMongoId(variableId))
            throw new InvalidOperationException("The mission completion variable identity is invalid.");
        var quest = pmc.Quests?.SingleOrDefault(q => q.QId.ToString() == mission.QuestId);
        // Mission access is permanent after acceptance. A later failed or
        // removed quest cannot prevent replay loot and mission finalization.
        if (
            quest == null
            || quest.Status
                is not (
                    SPTarkov.Server.Core.Models.Enums.QuestStatusEnum.Started
                    or SPTarkov.Server.Core.Models.Enums.QuestStatusEnum.AvailableForFinish
                    or SPTarkov.Server.Core.Models.Enums.QuestStatusEnum.Success
                )
        )
            return;

        var state = StoryStore.Read(pmc, seasonId);
        state.Variables ??= new();
        var required = Math.Clamp((int)Math.Ceiling(condition.Value ?? 1), 1, int.MaxValue);
        var changed = !state.Variables.TryGetValue(variableId, out var current) || current < required;
        if (changed)
            state.Variables[variableId] = required;

        if (quest.Status is not SPTarkov.Server.Core.Models.Enums.QuestStatusEnum.Success)
        {
            quest.CompletedConditions ??= [];
            changed |= !quest.CompletedConditions.Contains(mission.CompletionConditionId);
            if (!quest.CompletedConditions.Contains(mission.CompletionConditionId))
                quest.CompletedConditions.Add(mission.CompletionConditionId);
        }

        if (changed)
        {
            state.Revision++;
            StoryStore.Write(pmc, state);
        }
    }
}
