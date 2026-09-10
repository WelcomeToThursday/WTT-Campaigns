using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Profile;
using WTT.Campaigns.Shared.Story;

namespace WTT.Campaigns.Server.Story;

public sealed partial class StoryService
{
    private StoryFacts Facts(string id, SptProfile profile, StoryProgress state, StoryDefinition definition, StoryRequest? request = null)
    {
        var facts = new StoryFacts
        {
            TraderId = state.Conversation?.TraderId ?? "",
            Scene = request?.Scene ?? "",
            Observation = request?.Observation,
            SessionVariables = new(_sessions.GetOrAdd(id, _ => new())),
            InRaid = state.Raid is { Finished: false },
            Location = state.Raid is { Finished: false } ? state.Raid.Location : "",
        };
        RefreshFacts(profile.CharacterData!.PmcData!, facts, definition, state);
        return facts;
    }

    private void RefreshFacts(PmcData pmc, StoryFacts facts, StoryDefinition definition, StoryProgress state)
    {
        facts.Level = pmc.Info?.Level ?? 0;
        facts.QuestStatuses = (pmc.Quests ?? []).ToDictionary(q => q.QId.ToString(), q => q.Status.ToString());
        facts.CompletedConditions = (pmc.Quests ?? []).SelectMany(q => q.CompletedConditions ?? []).ToHashSet();
        facts.ConditionCounters = (pmc.TaskConditionCounters ?? []).ToDictionary(c => c.Key.ToString(), c => c.Value.Value ?? 0);
        facts.TraderReputation = (pmc.TradersInfo ?? []).ToDictionary(t => t.Key.ToString(), t => t.Value.Standing ?? 0);
        facts.TraderLoyalty = (pmc.TradersInfo ?? []).ToDictionary(t => t.Key.ToString(), t => (double)(t.Value.LoyaltyLevel ?? 0));
        facts.Items = (pmc.Inventory?.Items ?? [])
            .GroupBy(i => i.Template.ToString())
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Upd?.StackObjectsCount ?? 1));
        facts.Skills = (pmc.Skills?.Common ?? []).ToDictionary(s => s.Id.ToString(), s => Math.Floor(s.Progress / 100));
        facts.HideoutAreas = (pmc.Hideout?.Areas ?? []).ToDictionary(a => ((int)a.Type).ToString(), a => (double)(a.Level ?? 0));
        var pockets = pmc.Inventory?.Items?.FirstOrDefault(i => i.SlotId == "Pockets" && i.ParentId == pmc.Inventory.Equipment.ToString());
        var slots =
            pockets == null
                ? []
                : templates
                    .Items.GetValueOrDefault(pockets.Template)
                    ?.Properties?.Slots?.Where(s => s.Name?.StartsWith("SpecialSlot", StringComparison.Ordinal) == true)
                    .Select(s => s.Name!)
                    .ToArray()
                    ?? [];
        facts.FreeSpecialSlots = slots.Count(slot =>
            pmc.Inventory!.Items!.All(i => i.ParentId != pockets!.Id.ToString() || i.SlotId != slot)
        );
        facts.HandoverItems.Clear();
        foreach (var quest in pmc.Quests ?? [])
        {
            foreach (var condition in templates.Quests.GetValueOrDefault(quest.QId)?.Conditions?.AvailableForFinish ?? [])
            {
                if (condition.ConditionType == "HandoverItem")
                {
                    facts.HandoverItems[condition.Id.ToString()] = HandoverItems(
                            pmc,
                            Newtonsoft.Json.JsonConvert.DeserializeObject<NativeCondition>(json.Serialize(condition)!)!
                        )
                        .Sum(i => i.Upd?.StackObjectsCount ?? 1);
                }
            }
        }
        if (facts.Observation is { } observation)
        {
            StoryObservationRules.Apply(facts, observation);
        }

        facts.TradersWithNewQuests.Clear();
        facts.AvailableQuestIds.Clear();
        foreach (var metadata in definition.Quests)
        {
            var status = facts.QuestStatuses.GetValueOrDefault(metadata.QuestId) ?? "Locked";
            if (status is not ("Locked" or "AvailableForStart"))
            {
                continue;
            }
            var template = repository.Runtime(state.SeasonId).Definition.Quests.FirstOrDefault(q => (string?)q.Id == metadata.QuestId);
            if (
                template != null
                && template
                    .Conditions.AvailableForStart.Where(c => c.IsNecessary != false)
                    .All(c => Condition(c, pmc, definition, state, facts))
            )
            {
                facts.AvailableQuestIds.Add(metadata.QuestId);
                facts.TradersWithNewQuests.Add((string)template.TraderId!);
            }
        }
    }

    private bool Condition(NativeCondition c, PmcData pmc, StoryDefinition definition, StoryProgress state, StoryFacts facts)
    {
        var id = (string?)c.Id ?? "";
        var kind = (string?)c.ConditionType ?? "";
        var target = c.Target?.Values.FirstOrDefault() ?? "";
        var value = (double?)c.Value ?? 1;
        var comparison = (string?)c.CompareMethod ?? ">=";
        bool Compare(double actual)
        {
            return StoryRules.Compare(actual, comparison, value);
        }
        // Once completed, a native objective remains complete unless the native raid lifecycle resets it.
        if (facts.CompletedConditions.Contains(id))
        {
            return true;
        }
        switch (kind)
        {
            case "Quest":
                var statuses =
                    c.Status?.Select(s =>
                            int.TryParse(s, out var code) ? ((SPTarkov.Server.Core.Models.Enums.QuestStatusEnum)code).ToString() : s
                        )
                        .ToArray()
                    ?? ["Success"];
                return statuses.Contains(facts.QuestStatuses.GetValueOrDefault(target) ?? "Locked");
            case "Level":
                return Compare(facts.Level);
            case "TraderLoyalty":
                return Compare(facts.TraderLoyalty.GetValueOrDefault(target));
            case "TraderStanding":
                return Compare(facts.TraderReputation.GetValueOrDefault(target));
            case "Skill":
                return Compare(facts.Skills.GetValueOrDefault(target));
            case "HideoutArea":
                return Compare(facts.HideoutAreas.GetValueOrDefault(target));
            case "GlobalVariableValue":
                return Compare(StoryRules.Variable(definition, state, facts, target));
            case "CompletableItem":
                return Compare(state.CompletedItems.Contains(target) ? 1 : 0);
            case "LocationTrigger":
                return Compare(state.CompletedBindings.Contains(target) ? 1 : 0);
            case "CompleteCondition":
                return Compare(facts.CompletedConditions.Contains(target) ? 1 : 0);
            case "FindItem":
            case "HasItem":
                var observedItems = facts.Observation?.Items.Select(ObservedItem).ToList();
                return Compare(
                    (observedItems ?? pmc.Inventory?.Items ?? []).Where(i => MatchesItem(i, c)).Sum(i => i.Upd?.StackObjectsCount ?? 1)
                );
            case "HandoverItem":
            case "CounterCreator":
            case "VisitPlace":
            case "LeaveItemAtLocation":
            case "LaunchFlare":
                return Compare(facts.ConditionCounters.GetValueOrDefault(id));
            default:
                throw new InvalidOperationException("Unsupported story quest condition: " + kind);
        }
    }
}
