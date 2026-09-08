using System.Collections;
using Newtonsoft.Json.Linq;
using SeasonalPerks.Shared.Seasons;
using SeasonalPerks.Shared.Story;

namespace SeasonalPerks.Server.Web.Authoring;

public sealed record ChapterDeletionUse(string Name, string Path, string Navigation);

public static class ChapterDeletion
{
    public static IReadOnlyList<ChapterDeletionUse> Check(SeasonDefinition season, string chapterId, string? destination)
    {
        var chapter =
            season.Story?.Chapters.FirstOrDefault(c => c.Id == chapterId) ?? throw new ArgumentException("Select an existing chapter.");
        if (destination != null && (destination == chapterId || season.Story!.Chapters.All(c => c.Id != destination)))
        {
            throw new ArgumentException("Choose another chapter for the quests and notes.");
        }

        var removed = new JArray(JObject.FromObject(chapter));
        if (destination == null)
        {
            var quests = season.Story!.Quests.Where(q => q.ChapterId == chapterId).Select(q => q.QuestId).ToHashSet();
            foreach (var id in quests)
            {
                removed.Add(new JObject { ["Id"] = id });
            }

            foreach (var quest in season.Quests.OfType<JObject>().Where(q => quests.Contains((string?)q["_id"] ?? "")))
            {
                removed.Add(quest.DeepClone());
            }

            foreach (var note in season.Story.Notes.Where(n => n.ChapterId == chapterId))
            {
                removed.Add(JObject.FromObject(note));
            }
        }

        var candidate = SeasonCompiler.Copy(season);
        Apply(candidate, chapterId, destination);
        return StoryAuthoring
            .Uses(candidate, new JObject { ["Records"] = removed })
            .Select(path => Describe(candidate, season, path))
            .ToArray();
    }

    public static IReadOnlyList<ChapterDeletionUse> Delete(SeasonDefinition season, string chapterId, string? destination)
    {
        var uses = Check(season, chapterId, destination);
        if (uses.Count == 0)
        {
            Apply(season, chapterId, destination);
        }

        return uses;
    }

    private static void Apply(SeasonDefinition season, string chapterId, string? destination)
    {
        var story = season.Story!;
        if (destination != null)
        {
            foreach (var quest in story.Quests.Where(q => q.ChapterId == chapterId))
            {
                quest.ChapterId = destination;
            }

            foreach (var note in story.Notes.Where(n => n.ChapterId == chapterId))
            {
                note.ChapterId = destination;
            }
        }
        else
        {
            var quests = story.Quests.Where(q => q.ChapterId == chapterId).Select(q => q.QuestId).ToHashSet();
            story.Quests.RemoveAll(q => q.ChapterId == chapterId);
            foreach (var quest in season.Quests.OfType<JObject>().Where(q => quests.Contains((string?)q["_id"] ?? "")).ToArray())
            {
                quest.Remove();
            }

            story.Notes.RemoveAll(n => n.ChapterId == chapterId);
        }

        story.Chapters.RemoveAll(c => c.Id == chapterId);
    }

    private static ChapterDeletionUse Describe(SeasonDefinition season, SeasonDefinition original, string path)
    {
        var match = System.Text.RegularExpressions.Regex.Match(path, @"^Story\.(\w+)\[(\d+)\]");
        if (
            match.Success
            && season.Story!.GetType().GetProperty(match.Groups[1].Value)?.GetValue(season.Story) is IList list
            && int.TryParse(match.Groups[2].Value, out var index)
            && index < list.Count
        )
        {
            var record = list[index]!;
            var originalList = (IList)original.Story!.GetType().GetProperty(match.Groups[1].Value)!.GetValue(original.Story)!;
            var originalIndex = originalList.Cast<object>().ToList().FindIndex(r => StoryAuthoring.Id(r) == StoryAuthoring.Id(record));
            path = "Story." + match.Groups[1].Value + "[" + originalIndex + "]" + path[match.Length..];
            return new(ReferenceNames.Label(season, record, (_, id) => id), path, "Story/" + StoryAuthoring.Id(record));
        }

        match = System.Text.RegularExpressions.Regex.Match(path, @"^Quests\[(\d+)\]");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var questIndex) && season.Quests[questIndex] is JObject quest)
        {
            var originalIndex = original.Quests.OfType<JObject>().ToList().FindIndex(q => (string?)q["_id"] == (string?)quest["_id"]);
            path = "Quests[" + originalIndex + "]" + path[match.Length..];
            return new(NativeQuestAuthoring.QuestName(quest), path, "Quests/" + (string?)quest["_id"]);
        }

        return new("Referenced by " + path, path, "");
    }
}
