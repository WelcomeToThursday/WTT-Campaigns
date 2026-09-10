using SeasonalPerks.Client.Story;
using SeasonalPerks.Shared.Story;

namespace SeasonalPerks.Tests;

internal static class StoryChapterNotificationChecks
{
    internal static void Run(
        Action<bool, string> check,
        Func<StoryResponse, IReadOnlyList<(StoryChapter Chapter, string Status)>>? accept = null,
        Action? reset = null
    )
    {
        var chapter = new StoryChapter { Id = "chapter", Name = "Factory Office" };
        var quest = new StoryQuest
        {
            QuestId = "quest",
            ChapterId = chapter.Id,
            Main = true,
        };
        var response = new StoryResponse
        {
            CharacterId = "character",
            SeasonId = "season",
            Definition = new() { Chapters = [chapter], Quests = [quest] },
            State = new(),
            Facts = new(),
        };
        if (accept == null)
        {
            var changes = new StoryChapterChanges();
            accept = changes.Accept;
            reset = changes.Reset;
        }
        check(accept(response).Count == 0, "Loading a locked chapter establishes a silent baseline");
        response.Facts.QuestStatuses[quest.QuestId] = "AvailableForStart";
        check(accept(response).Count == 0, "Stored availability alone does not bypass an unmet zone prerequisite");
        response.Facts.AvailableQuestIds.Add(quest.QuestId);
        check(accept(response).Single().Status == "Started", "Factory zone unlock announces chapter start without a revision change");
        check(
            response.Facts.QuestStatuses[quest.QuestId] == "AvailableForStart",
            "Chapter notification does not auto-accept a manual quest"
        );
        check(accept(response).Count == 0, "Repeated observation and journal read responses do not repeat the toast");
        response.Facts.AvailableQuestIds.Clear();
        accept(response);
        response.Facts.AvailableQuestIds.Add(quest.QuestId);
        check(accept(response).Count == 0, "Leaving and reentering a zone does not repeat chapter start");
        response.Facts.QuestStatuses[quest.QuestId] = "Started";
        check(accept(response).Count == 0, "Accepting an unlocked quest does not announce the same chapter twice");
        response.Facts.QuestStatuses[quest.QuestId] = "Success";
        check(accept(response).Single().Status == "Complete", "Completing required quests announces chapter completion");
        check(accept(response).Count == 0, "Replayed completion is silent");
        reset!();
        check(accept(response).Count == 0, "Reconnect does not replay previously completed chapters");
        response.CharacterId = "other-character";
        check(accept(response).Count == 0, "Character switch establishes its own baseline");
        response.SeasonId = "other-season";
        check(accept(response).Count == 0, "Season switch establishes its own baseline");
        response.Definition.Chapters.Add(
            new()
            {
                Id = "hidden",
                Visibility = new() { Type = "Level", Value = 5 },
            }
        );
        response.Definition.Quests.Add(
            new()
            {
                QuestId = "hidden-quest",
                ChapterId = "hidden",
                Main = true,
            }
        );
        response.Facts.QuestStatuses["hidden-quest"] = "Started";
        check(accept(response).Count == 0, "Hidden chapters are not announced");
        response.Facts.Level = 5;
        check(accept(response).Single().Chapter.Id == "hidden", "A newly revealed active chapter is announced");
        response.Facts.QuestStatuses["hidden-quest"] = "Fail";
        response.Revision = 2;
        check(accept(response).Single().Status == "Failed", "Required quest failure announces chapter failure");
        response.Revision = 1;
        response.Facts.QuestStatuses["hidden-quest"] = "Success";
        check(accept(response).Count == 0, "Stale responses cannot generate a later notification");
        response.Revision = 3;
        response.Definition.Chapters.Add(new() { Id = "empty" });
        response.Definition.Quests.Add(
            new()
            {
                QuestId = "optional",
                ChapterId = chapter.Id,
                Main = false,
            }
        );
        response.Facts.QuestStatuses["optional"] = "Fail";
        var accepted = accept(response);
        check(
            accepted.Count == 1 && accepted[0].Chapter.Id == "hidden",
            "Empty chapters and optional failures do not produce false starts or failures"
        );
    }
}
