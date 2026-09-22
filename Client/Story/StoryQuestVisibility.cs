using WTT.Campaigns.Shared.Story;

namespace WTT.Campaigns.Client.Story;

internal static class StoryQuestVisibility
{
    internal static bool Allows(StoryResponse? response, string characterId, string seasonId, string questId)
    {
        // Wait for this character's membership before exposing native trader actions.
        if (response?.Definition == null || response.CharacterId != characterId || response.SeasonId != seasonId)
            return false;
        if (response.MissionQuestIds.Contains(questId))
            return true;
        foreach (var quest in response.Definition.Quests)
            if (quest.QuestId == questId)
                return false;
        return true;
    }
}
