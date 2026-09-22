using WTT.Campaigns.Client.Story;
using WTT.Campaigns.Shared.Story;

namespace WTT.Campaigns.Tests;

internal static class StoryQuestVisibilityChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var response = new StoryResponse
        {
            CharacterId = "character",
            SeasonId = "campaign",
            Definition = new StoryDefinition
            {
                Quests =
                [
                    new() { QuestId = "story" },
                    new() { QuestId = "hidden-story", Hidden = true },
                    new() { QuestId = "optional-story", Main = false },
                    new() { QuestId = "mission-unlock" },
                ],
            },
            Facts = new StoryFacts(),
            MissionQuestIds = ["mission-unlock"],
        };
        foreach (var status in new[] { "Locked", "AvailableForStart", "Started", "AvailableForFinish", "Success", "Fail" })
        {
            foreach (var quest in response.Definition.Quests.Where(q => q.QuestId != "mission-unlock"))
            {
                response.Facts.QuestStatuses[quest.QuestId] = status;
                check(!Allows(quest.QuestId), "Trader list excludes " + quest.QuestId + " in " + status);
                check(response.Facts.QuestStatuses[quest.QuestId] == status, "Filtering preserves native story progress");
            }
            check(Allows("mission-unlock"), "Mission unlock tasks with story membership remain in trader lists in " + status);
            check(Allows("ordinary"), "Ordinary trader tasks remain in trader lists in " + status);
            check(Allows("daily"), "Repeatable tasks remain in trader lists in " + status);
        }
        check(!StoryQuestVisibility.Allows(null, "character", "campaign", "story"), "Initial story load cannot leak trader actions");
        check(!StoryQuestVisibility.Allows(response, "other", "campaign", "story"), "Character switches reject stale membership");
        check(!StoryQuestVisibility.Allows(response, "character", "other", "story"), "Campaign switches reject stale membership");
        response.Definition.Quests.Clear();
        check(Allows("story") && Allows("mission-unlock"), "Campaigns without story membership retain native quests");

        bool Allows(string questId) => StoryQuestVisibility.Allows(response, "character", "campaign", questId);
    }
}
