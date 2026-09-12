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

        var authored = new SeasonDefinition { Story = new() };
        var dialog = StoryAuthoring.AddConversation(authored, "111111111111111111111111", true);
        var source = dialog.Lines[0];
        var existingReply = dialog.Lines[1];
        var originalReplyTrigger = JsonConvert.SerializeObject(existingReply.Trigger);
        var added = ConversationAuthoring.AddAfter(authored.Story, dialog, source, "Player");
        check(added.Trigger.Value == existingReply.Trigger.Value, "An additional player reply joins the existing branch phase");
        check(
            JsonConvert.SerializeObject(existingReply.Trigger) == originalReplyTrigger,
            "Adding a reply preserves existing reply conditions"
        );
        var accept = new StoryAction
        {
            Id = StoryAuthoring.NewId(),
            Type = StoryActionType.AcceptQuest,
            QuestId = "222222222222222222222222",
        };
        added.Actions.Add(accept);
        var continuation = ConversationAuthoring.AddAfter(authored.Story, dialog, added, "Npc");
        check(continuation.Trigger.Value != added.Trigger.Value, "New continuation allocates a different phase");
        check(
            continuation.Actions.Any(a => a.Type == StoryActionType.SetVariable && a.Value != continuation.Trigger.Value),
            "New automatic trader line advances its phase instead of repeating"
        );
        check(added.Actions[0] == accept, "Connecting dialogue preserves preceding quest effects");
        ConversationAuthoring.End(authored.Story, dialog, added);
        check(
            added.Actions[0] == accept && added.Actions.Last().Type == StoryActionType.QuitAction,
            "Ending a conversation keeps quest effects before close"
        );
        check(!added.Actions.Any(a => a.Type == StoryActionType.SetVariable), "Ending removes the phase advance");
        var terminalSnapshot = JsonConvert.SerializeObject(authored);
        try
        {
            ConversationAuthoring.AddAfter(authored.Story, dialog, added, "Npc");
            check(false, "Terminal line must reject a continuation");
        }
        catch (InvalidOperationException)
        {
            check(JsonConvert.SerializeObject(authored) == terminalSnapshot, "Rejected continuation preserves every draft record");
        }
        ConversationAuthoring.Connect(authored.Story, dialog, added, continuation);
        check(
            !added.Actions.Any(a => a.Type == StoryActionType.QuitAction) && added.Actions[0] == accept,
            "Explicit reconnection replaces close and retains quest effect"
        );
        check(ConversationAuthoring.NextLines(dialog, added).Contains(continuation), "Outline follows saved phase actions");
        authored.Story.Variables.Single(v => v.Id == dialog.MainVariable).Scope = StoryVariableScope.Profile;
        var importedSnapshot = JsonConvert.SerializeObject(authored);
        try
        {
            ConversationAuthoring.End(authored.Story, dialog, added);
            check(false, "Shared profile phase must not be rewritten");
        }
        catch (InvalidOperationException)
        {
            check(JsonConvert.SerializeObject(authored) == importedSnapshot, "Imported profile state is preserved by guided editing guard");
        }
        var summarySnapshot = JsonConvert.SerializeObject(authored);
        var level = NativeQuestAuthoring.Condition("Level");
        level.Value = 10;
        check(
            AuthoringSummary.Objective(authored, level, (_, id) => id).Contains(">= 10"),
            "Unlock summary reflects saved level comparison"
        );
        check(
            AuthoringSummary.Condition(authored, new StoryCondition(), (_, id) => id) == "Always",
            "Empty All summary explains unconditional visibility"
        );
        check(
            JsonConvert.SerializeObject(authored) == summarySnapshot,
            "Generating conversation and quest summaries does not mutate the draft"
        );

        var deletionSeason = new SeasonDefinition { Story = new() };
        var keptQuest = NativeQuestAuthoring.Create();
        deletionSeason.Quests.Add(keptQuest);
        var deletedDialog = StoryAuthoring.AddConversation(deletionSeason, "111111111111111111111111", true, (string)keptQuest.Id!);
        var keptDialog = StoryAuthoring.AddConversation(deletionSeason, "111111111111111111111111", false);
        var deletionSnapshot = JsonConvert.SerializeObject(deletionSeason);
        var plan = ConversationDeletion.Check(deletionSeason, deletedDialog.Id);
        check(
            plan.Uses.Count == 0 && plan.EntryPoints == 1 && plan.RemovePhase,
            "A template's own entry point does not block conversation deletion"
        );
        check(JsonConvert.SerializeObject(deletionSeason) == deletionSnapshot, "Opening deletion preview leaves the draft unchanged");
        var raid = new StoryRaidBinding { Id = StoryAuthoring.NewId(), EntryPointId = deletionSeason.Story.EntryPoints[0].Id };
        deletionSeason.Story.RaidBindings.Add(raid);
        var blockedSnapshot = JsonConvert.SerializeObject(deletionSeason);
        plan = ConversationDeletion.Delete(deletionSeason, deletedDialog.Id);
        check(plan.Uses.Any(u => u.Navigation == "Story/" + raid.Id), "An external raid reference links to the blocking raid record");
        check(
            JsonConvert.SerializeObject(deletionSeason) == blockedSnapshot,
            "A reference added after preview prevents all deletion mutations"
        );
        deletionSeason.Story.RaidBindings.Clear();
        var switchAction = new StoryAction
        {
            Id = StoryAuthoring.NewId(),
            Type = StoryActionType.SwitchDialog,
            Target = deletedDialog.Id,
        };
        keptDialog.Lines[0].Actions.Add(switchAction);
        check(
            ConversationDeletion.Check(deletionSeason, deletedDialog.Id).Uses.Any(u => u.Navigation == "Story/" + keptDialog.Id),
            "Incoming dialogue transitions prevent broken conversations"
        );
        keptDialog.Lines[0].Actions.Remove(switchAction);
        var sharedPhase = new StoryAction
        {
            Id = StoryAuthoring.NewId(),
            Type = StoryActionType.SetVariable,
            Target = deletedDialog.MainVariable,
        };
        keptDialog.Lines[0].Actions.Add(sharedPhase);
        check(
            !ConversationDeletion.Check(deletionSeason, deletedDialog.Id).RemovePhase,
            "A phase used by another conversation is retained"
        );
        keptDialog.Lines[0].Actions.Remove(sharedPhase);
        var keptSnapshot = JsonConvert.SerializeObject(keptDialog);
        ConversationDeletion.Delete(deletionSeason, deletedDialog.Id);
        check(
            !deletionSeason.Story.Dialogs.Contains(deletedDialog)
                && deletionSeason.Story.EntryPoints.All(e => e.DialogId != deletedDialog.Id),
            "Confirmed deletion removes dialogue and its entry points together"
        );
        check(deletionSeason.Story.Variables.All(v => v.Id != deletedDialog.MainVariable), "Unreferenced conversation phase is cleaned up");
        check(
            deletionSeason.Quests.Single() == keptQuest && JsonConvert.SerializeObject(keptDialog) == keptSnapshot,
            "Conversation deletion preserves quests and other dialogue"
        );
    }
}
