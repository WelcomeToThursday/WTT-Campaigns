using Newtonsoft.Json;
using WTT.Campaigns.Server.Web.Authoring;
using WTT.Campaigns.Shared.Seasons;
using WTT.Campaigns.Shared.Story;

namespace WTT.Campaigns.Tests;

internal static class DialogueEffectChecks
{
    internal static void Run(Action<bool, string> check)
    {
        const string trader = "54cb50c76803fa8b248b4571";
        var standing = new StoryAction { Id = StoryAuthoring.NewId(), Type = StoryActionType.TraderStanding, Target = trader, StandingChange = -0.02 };
        var restored = JsonConvert.DeserializeObject<StoryAction>(JsonConvert.SerializeObject(standing))!;
        check(restored.Type == StoryActionType.TraderStanding && restored.StandingChange == -0.02, "Dialogue standing effect preserves fractional negative reputation through serialization");
        check(StoryAuthoring.ReferenceKind(standing, "Target") == "traders" && StoryAuthoring.Visible(standing, "StandingChange") && !StoryAuthoring.Visible(standing, "Value") && !StoryAuthoring.Visible(standing, "QuestId"), "Standing effect exposes trader and decimal amount without variable or quest fields");
        var fail = new StoryAction { Id = StoryAuthoring.NewId(), Type = StoryActionType.FailQuest };
        check(StoryAuthoring.ReferenceKind(fail, "QuestId") == "ownedquests" && StoryAuthoring.Visible(fail, "QuestId") && !StoryAuthoring.Visible(fail, "StandingChange"), "Fail quest effect offers owned quest selection");

        var season = new SeasonDefinition { Id = StoryAuthoring.NewId(), Story = new() };
        var quest = NativeQuestAuthoring.Create();
        season.Quests.Add(quest);
        fail.QuestId = (string)quest.Id!;
        season.Story.Quests.Add(new() { QuestId = fail.QuestId });
        var reply = new StoryDialogLine { Id = StoryAuthoring.NewId(), Side = "Player", Text = "Apply effects", Actions = [standing, fail] };
        var dialog = new StoryDialog { Id = StoryAuthoring.NewId(), TraderId = trader, Lines = [reply] };
        var entry = new StoryEntryPoint { Id = StoryAuthoring.NewId(), TraderId = trader, DialogId = dialog.Id };
        season.Story.Dialogs.Add(dialog);
        season.Story.EntryPoints.Add(entry);
        StoryRehearsal Run(string status)
        {
            var run = new StoryRehearsal(season, new() { TraderId = trader, TraderReputation = new() { [trader] = 0.2 }, QuestStatuses = new() { [fail.QuestId] = status } }, 1);
            run.Start(entry.Id);
            run.Select(reply.Id);
            return run;
        }
        var active = Run("Started");
        check(active.Error.Length == 0 && active.Facts.TraderReputation[trader] == 0.18 && active.Facts.QuestStatuses[fail.QuestId] == "Fail", "Dialogue effects lower reputation and fail an active quest in rehearsal");
        check(active.Log.Any(l => l.Contains("failure rewards")), "Rehearsal records native failure rewards");
        standing.StandingChange = 0.02;
        var ready = Run("AvailableForFinish");
        check(ready.Error.Length == 0 && ready.Facts.TraderReputation[trader] == 0.22 && ready.Facts.QuestStatuses[fail.QuestId] == "Fail", "Dialogue effects increase reputation and fail a ready quest");
        foreach (var status in new[] { "Success", "Locked", "AvailableForStart" })
        {
            var blocked = Run(status);
            check(blocked.Error.Length > 0 && blocked.Facts.TraderReputation[trader] == 0.2 && blocked.Facts.QuestStatuses[fail.QuestId] == status, "Invalid failure rolls back earlier standing effect: " + status);
        }
        check(Run("Fail").Error.Length == 0, "Already failed quest is an idempotent no-op");
        standing.StandingChange = double.NaN;
        var invalid = Run("Started");
        check(invalid.Error.Length > 0 && invalid.Facts.QuestStatuses[fail.QuestId] == "Started", "Non-finite standing is rejected before quest mutation");
    }
}
