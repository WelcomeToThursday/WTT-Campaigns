using SeasonalPerks.Shared.Story;

namespace SeasonalPerks.Client.Story;

// Compare accepted server snapshots, including observation-only refreshes whose revision may be unchanged.
internal sealed class StoryChapterChanges
{
    private string _character = "";
    private string _season = "";
    private long _revision = -1;
    private readonly HashSet<string> _announced = new();

    internal void Reset()
    {
        _character = "";
        _season = "";
        _revision = -1;
        _announced.Clear();
    }

    internal IReadOnlyList<(StoryChapter Chapter, string Status)> Accept(StoryResponse response)
    {
        var changes = new List<(StoryChapter, string)>();
        if (response.Definition == null || response.State == null || response.Facts == null)
        {
            return changes;
        }
        var initial = _character != response.CharacterId || _season != response.SeasonId;
        if (initial)
        {
            Reset();
            _character = response.CharacterId;
            _season = response.SeasonId;
        }
        if (response.Revision < _revision)
        {
            return changes;
        }
        _revision = response.Revision;
        foreach (var chapter in response.Definition.Chapters.OrderBy(c => c.Order))
        {
            if (!StoryRules.Evaluate(chapter.Visibility, response.Definition, response.State, response.Facts))
            {
                continue;
            }
            var quests = response.Definition.Quests.Where(q => q.ChapterId == chapter.Id).ToArray();
            var status =
                StoryRules.ChapterComplete(chapter, response.Definition, response.Facts) ? "Complete"
                : quests.Any(q =>
                    q.Main && response.Facts.QuestStatuses.GetValueOrDefault(q.QuestId) is "Fail" or "MarkedAsFailed" or "Expired"
                )
                    ? "Failed"
                : quests.Any(q =>
                    !q.Hidden
                    && StoryRules.Evaluate(q.Visibility, response.Definition, response.State, response.Facts)
                    && (
                        response.Facts.AvailableQuestIds.Contains(q.QuestId)
                        || response.Facts.QuestStatuses.GetValueOrDefault(q.QuestId) is "Started" or "AvailableForFinish" or "Success"
                    )
                )
                    ? "Started"
                : "";
            if (status.Length == 0)
            {
                continue;
            }
            var first = _announced.Add(chapter.Id + ":" + status);
            // A chapter first seen in a terminal state must never announce a late start.
            _announced.Add(chapter.Id + ":Started");
            if (!initial && first)
            {
                changes.Add((chapter, status));
            }
        }
        return changes;
    }
}
