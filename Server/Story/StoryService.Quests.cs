using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Inventory;
using SPTarkov.Server.Core.Models.Eft.Quests;
using SPTarkov.Server.Core.Models.Enums;
using WTT.Campaigns.Shared.Story;

namespace WTT.Campaigns.Server.Story;

public sealed partial class StoryService
{
    private NativeQuest QuestTemplate(StoryProgress state, StoryDefinition definition, string questId)
    {
        if (!definition.Quests.Any(q => q.QuestId == questId))
        {
            throw new InvalidOperationException("The quest does not belong to this story.");
        }
        return repository.Runtime(state.SeasonId).Definition.Quests.SingleOrDefault(q => (string?)q.Id == questId)
            ?? throw new InvalidOperationException("External quests are read-only story dependencies.");
    }

    private void NativeAction(
        string id,
        PmcData pmc,
        StoryDefinition definition,
        StoryProgress state,
        StoryFacts facts,
        StoryAction action,
        IReadOnlyCollection<string>? selectedItems = null
    )
    {
        var questId = action.QuestId.Length > 0 ? action.QuestId : state.Conversation?.SelectedQuestId ?? "";
        var template = QuestTemplate(state, definition, questId);
        var quest = pmc.Quests?.FirstOrDefault(q => q.QId.ToString() == questId);
        bool Stage(string stage)
        {
            return template
                .Conditions.Stage(stage)
                .Where(c => (bool?)c.IsNecessary != false)
                .All(c => Condition(c, pmc, definition, state, facts));
        }
        switch (action.Type)
        {
            case StoryActionType.AcceptQuest:
                if (quest?.Status is QuestStatusEnum.Started or QuestStatusEnum.AvailableForFinish or QuestStatusEnum.Success)
                {
                    return;
                }
                if (quest?.Status is QuestStatusEnum.Fail or QuestStatusEnum.MarkedAsFailed || !Stage("AvailableForStart"))
                {
                    throw new InvalidOperationException("This story quest is not available to accept.");
                }
                quests.AcceptQuest(pmc, new AcceptQuestRequestData { QuestId = new MongoId(questId) }, new MongoId(id));
                if (pmc.Quests?.Any(q => q.QId.ToString() == questId && q.Status == QuestStatusEnum.Started) != true)
                {
                    throw new InvalidOperationException("The native quest system rejected this quest.");
                }
                break;
            case StoryActionType.FinishQuest:
            case StoryActionType.PlayerReward:
                if (quest?.Status == QuestStatusEnum.Success)
                {
                    return;
                }
                if (quest?.Status is not (QuestStatusEnum.Started or QuestStatusEnum.AvailableForFinish) || !Stage("AvailableForFinish"))
                {
                    throw new InvalidOperationException("Complete the required objectives before finishing this quest.");
                }
                quests.CompleteQuest(
                    pmc,
                    new CompleteQuestRequestData { QuestId = new MongoId(questId), RemoveExcessItems = false },
                    new MongoId(id)
                );
                break;
            case StoryActionType.HandoverItem:
                if (facts.InRaid || quest?.Status is not (QuestStatusEnum.Started or QuestStatusEnum.AvailableForFinish))
                {
                    throw new InvalidOperationException("Quest items can only be handed over for an active quest outside a raid.");
                }
                var condition = template.Conditions.AvailableForFinish.Single(c =>
                    (string?)c.Id == action.ConditionId && (string?)c.ConditionType == "HandoverItem"
                );
                var remaining = (double)condition.Value! - facts.ConditionCounters.GetValueOrDefault(action.ConditionId);
                var items = new List<IdWithCount>();
                foreach (
                    var item in HandoverItems(pmc, condition)
                        .Where(i => selectedItems?.Count > 0 ? selectedItems.Contains(i.Id.ToString()) : AutomaticHandover(i))
                )
                {
                    if (remaining <= 0)
                    {
                        break;
                    }
                    var count = item.Upd?.StackObjectsCount ?? 1;
                    items.Add(new IdWithCount { Id = item.Id, Count = count });
                    remaining -= count;
                }
                if (items.Count == 0)
                {
                    throw new InvalidOperationException("No eligible items are available for this handover.");
                }
                quests.HandoverQuest(
                    pmc,
                    new HandoverQuestRequestData
                    {
                        QuestId = new MongoId(questId),
                        ConditionId = new MongoId(action.ConditionId),
                        Items = items,
                    },
                    new MongoId(id)
                );
                break;
            default:
                throw new InvalidOperationException("No native story adapter is registered for " + action.Type + ".");
        }
    }

    private void Reconcile(string id, PmcData pmc, StoryDefinition definition, StoryProgress state, StoryFacts facts)
    {
        // Bounded fixed point supports chapter chains while rejecting automatic cycles.
        var limit = definition.Quests.Count + repository.Runtime(state.SeasonId).Definition.Quests.Sum(q => q.AllConditions().Count());
        for (var pass = 0; pass <= limit; pass++)
        {
            var changed = false;
            foreach (var metadata in definition.Quests)
            {
                var status = facts.QuestStatuses.GetValueOrDefault(metadata.QuestId) ?? "Locked";
                var template = repository.Runtime(state.SeasonId).Definition.Quests.SingleOrDefault(q => (string?)q.Id == metadata.QuestId);
                if (template == null)
                {
                    continue;
                }
                bool Stage(string stage)
                {
                    return template
                        .Conditions.Stage(stage)
                        .Where(c => (bool?)c.IsNecessary != false)
                        .All(c => Condition(c, pmc, definition, state, facts));
                }
                if (
                    metadata.AutoStart
                    && status is "Locked" or "AvailableForStart"
                    && Stage("AvailableForStart")
                    && StoryRules.Evaluate(metadata.Visibility, definition, state, facts)
                )
                {
                    NativeAction(
                        id,
                        pmc,
                        definition,
                        state,
                        facts,
                        new StoryAction { Type = StoryActionType.AcceptQuest, QuestId = metadata.QuestId }
                    );
                    changed = true;
                    RefreshFacts(pmc, facts, definition, state);
                }
                if (facts.QuestStatuses.GetValueOrDefault(metadata.QuestId) is "Started" or "AvailableForFinish")
                {
                    var quest = pmc.Quests!.Single(q => q.QId.ToString() == metadata.QuestId);
                    quest.CompletedConditions ??= [];
                    foreach (var condition in template.Conditions.AvailableForFinish)
                    {
                        var conditionId = (string)condition.Id!;
                        if (!quest.CompletedConditions.Contains(conditionId) && Condition(condition, pmc, definition, state, facts))
                        {
                            quest.CompletedConditions.Add(conditionId);
                            facts.CompletedConditions.Add(conditionId);
                            changed = true;
                        }
                    }
                    if (template.Conditions.Fail.Any(c => Condition(c, pmc, definition, state, facts)))
                    {
                        quests.FailQuest(
                            pmc,
                            new FailQuestRequestData { QuestId = quest.QId, RemoveExcessItems = false },
                            new MongoId(id),
                            StoryNativeScope.Current!.Output
                        );
                        changed = true;
                        RefreshFacts(pmc, facts, definition, state);
                    }
                }
                if (
                    metadata.AutoComplete
                    && facts.QuestStatuses.GetValueOrDefault(metadata.QuestId) is "Started" or "AvailableForFinish"
                    && Stage("AvailableForFinish")
                )
                {
                    NativeAction(
                        id,
                        pmc,
                        definition,
                        state,
                        facts,
                        new StoryAction { Type = StoryActionType.FinishQuest, QuestId = metadata.QuestId }
                    );
                    changed = true;
                    RefreshFacts(pmc, facts, definition, state);
                }
                var current = facts.QuestStatuses.GetValueOrDefault(metadata.QuestId) ?? "Locked";
                if (metadata.StatusNotes.TryGetValue(current, out var noteIds))
                {
                    foreach (var noteId in noteIds)
                    {
                        state.Notes.TryAdd(noteId, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                    }
                }
            }
            foreach (
                var note in definition.Notes.Where(n => n.ConditionIds.Count > 0 && n.ConditionIds.All(facts.CompletedConditions.Contains))
            )
            {
                state.Notes.TryAdd(note.Id, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            }
            if (!changed)
            {
                return;
            }
        }
        throw new InvalidOperationException("Automatic story quests did not reach a stable state.");
    }
}
