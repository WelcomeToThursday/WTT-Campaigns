using Newtonsoft.Json.Linq;
using SeasonalPerks.Shared.Seasons;
using SeasonalPerks.Shared.Story;

namespace SeasonalPerks.Server.Web.Authoring;

public static class QuestStoryFlow
{
    public static string ChapterForQuest(SeasonDefinition season, string questId)
    {
        var membership = season.Story?.Quests.FirstOrDefault(q => q.QuestId == questId);
        return membership != null && season.Story!.Chapters.Any(c => c.Id == membership.ChapterId) ? membership.ChapterId : "";
    }

    public static IEnumerable<JObject> Quests(SeasonDefinition season, string chapterId)
    {
        return season.Quests.OfType<JObject>().Where(q => ChapterForQuest(season, (string?)q["_id"] ?? "") == chapterId);
    }

    public static void AssignQuest(SeasonDefinition season, string questId, string chapterId)
    {
        if (
            season.Story?.Chapters.Any(c => c.Id == chapterId) != true
            || !season.Quests.OfType<JObject>().Any(q => (string?)q["_id"] == questId)
        )
        {
            throw new ArgumentException("Choose an existing quest and chapter.");
        }

        var membership = season.Story.Quests.FirstOrDefault(q => q.QuestId == questId);
        if (membership == null)
        {
            season.Story.Quests.Add(new() { QuestId = questId, ChapterId = chapterId });
        }
        else
        {
            membership.ChapterId = chapterId;
        }
    }

    public static JObject DuplicateQuest(SeasonDefinition season, JObject quest)
    {
        var copy = (JObject)StoryAuthoring.Duplicate(quest);
        var membership = season.Story?.Quests.FirstOrDefault(q => q.QuestId == (string?)quest["_id"]);
        season.Quests.Add(copy);
        if (membership != null)
        {
            var copyMembership = SeasonCompiler.Copy(membership);
            copyMembership.QuestId = (string)copy["_id"]!;
            season.Story!.Quests.Add(copyMembership);
        }

        return copy;
    }

    public static IReadOnlyList<string> DeleteQuest(SeasonDefinition season, JObject quest)
    {
        // The quest's own chapter membership is removed with it. Every other reference still blocks deletion.
        var candidate = SeasonCompiler.Copy(season);
        candidate.Story?.Quests.RemoveAll(q => q.QuestId == (string?)quest["_id"]);
        var uses = StoryAuthoring.Uses(candidate, quest);
        if (uses.Count > 0)
        {
            return uses;
        }

        season.Story?.Quests.RemoveAll(q => q.QuestId == (string?)quest["_id"]);
        quest.Remove();
        return [];
    }

    public static StoryChapter CreateChapter(SeasonDefinition season, string questId)
    {
        if (!season.Quests.OfType<JObject>().Any(q => (string?)q["_id"] == questId))
        {
            throw new ArgumentException("Select a native quest first.");
        }

        var story = season.Story ??= new();
        var chapter = new StoryChapter
        {
            Id = StoryAuthoring.NewId(),
            Name = "New chapter",
            Order = story.Chapters.Count,
        };
        story.Chapters.Add(chapter);
        var membership = story.Quests.FirstOrDefault(q => q.QuestId == questId);
        if (membership == null)
        {
            story.Quests.Add(new() { QuestId = questId, ChapterId = chapter.Id });
        }
        else
        {
            membership.ChapterId = chapter.Id;
        }

        return chapter;
    }

    public static JObject AddQuest(SeasonDefinition season, string chapterId)
    {
        if (season.Story?.Chapters.Any(c => c.Id == chapterId) != true)
        {
            throw new ArgumentException("Select a chapter first.");
        }

        var quest = NativeQuestAuthoring.Create();
        season.Quests.Add(quest);
        season.Story.Quests.Add(new() { QuestId = (string)quest["_id"]!, ChapterId = chapterId });
        return quest;
    }

    public static StoryNote AddNote(SeasonDefinition season, StoryQuest quest, string status)
    {
        if (!StoryRules.QuestStatuses.Contains(status) || season.Story?.Quests.Contains(quest) != true)
        {
            throw new ArgumentException("Choose a story quest and a supported status.");
        }

        var note = new StoryNote
        {
            Id = StoryAuthoring.NewId(),
            ChapterId = quest.ChapterId,
            Text = "New journal note",
        };
        season.Story.Notes.Add(note);
        if (!quest.StatusNotes.TryGetValue(status, out var notes))
        {
            quest.StatusNotes[status] = notes = new();
        }

        notes.Add(note.Id);
        return note;
    }
}
