using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WTT.Campaigns.Server.Web.Authoring;
using WTT.Campaigns.Shared.Seasons;
using WTT.Campaigns.Shared.Story;

namespace WTT.Campaigns.Tests;

internal static class AuthoringChecks
{
    public static void Run(Action<bool, string> check)
    {
        var traderLocale = new Dictionary<string, string>
        {
            ["trader Nickname"] = "Prapor",
            ["trader FullName"] = "Pavel",
            ["item Name"] = "Medical kit",
        };
        check(
            ReferenceNames.Localized("traders", "trader", "trader", key => traderLocale.GetValueOrDefault(key)) == "Prapor",
            "Trader names prefer the nickname locale used by SPT"
        );
        traderLocale["trader Nickname"] = "";
        check(
            ReferenceNames.Localized("traders", "trader", "trader", key => traderLocale.GetValueOrDefault(key)) == "Pavel",
            "Blank trader nickname falls back to full name"
        );
        check(
            ReferenceNames.Localized("items", "item", "item", key => traderLocale.GetValueOrDefault(key)) == "Medical kit",
            "Item names retain their own locale convention"
        );
        check(
            ReferenceNames.Localized("items", "missing", "Imported model", key => null) == "Imported model",
            "Missing localization retains installed fallback"
        );
        var flow = new SeasonDefinition();
        var firstQuest = NativeQuestAuthoring.Create();
        flow.Quests.Add(firstQuest);
        var firstChapter = QuestStoryFlow.CreateChapter(flow, (string)firstQuest.Id!);
        check(flow.Story!.Quests.Single().ChapterId == firstChapter.Id, "Inline chapter creation assigns the current native quest");
        var secondQuest = QuestStoryFlow.AddQuest(flow, firstChapter.Id);
        check(
            flow.Story.Quests.Count == 2 && flow.Story.Quests.Last().QuestId == (string)secondQuest.Id!,
            "Chapter creation flow creates one linked native quest and membership"
        );
        var flowNote = QuestStoryFlow.AddNote(flow, flow.Story.Quests[0], "Success");
        check(
            flowNote.ChapterId == firstChapter.Id && flow.Story.Quests[0].StatusNotes["Success"].Single() == flowNote.Id,
            "Inline note creation links chapter and requested status"
        );
        check(StoryAuthoring.Uses(flow, flowNote).Count == 1, "Inline linked notes retain deletion protection");
        NativeQuestAuthoring.QuestText(firstQuest, "name", "Supplies for the trader");
        check(
            ReferenceNames.Resolve(flow, "quests", (string)firstQuest.Id!, (_, _) => "Installed old name") == "Supplies for the trader",
            "Unsaved authored names override installed content labels"
        );
        var flowBefore = JsonConvert.SerializeObject(flow);
        try
        {
            QuestStoryFlow.AddQuest(flow, "missing");
        }
        catch (ArgumentException) { }

        try
        {
            QuestStoryFlow.AddNote(flow, flow.Story.Quests[0], "invalid");
        }
        catch (ArgumentException) { }

        check(flowBefore == JsonConvert.SerializeObject(flow), "Invalid inline creation leaves the draft unchanged");
        var flowCopy = SeasonCompiler.Copy(flow);
        check(
            SeasonCompiler.Texts(flowCopy)[flowNote.Id + " text"] == flowNote.Text && flowCopy.Story!.Quests.Count == 2,
            "Inline story records survive lossless draft copy and localization"
        );
        check(
            StoryAuthoring.Help(new StoryCondition { Type = "TraderReputation" }, "Value").Contains("0.2")
                && StoryAuthoring.Help(new StoryRandomGate(), "Start").Contains("Inclusive"),
            "Condition hints distinguish reputation and random gate ranges"
        );
        var scoped = SeasonCompiler.Copy(flow);
        var chapterTwo = new StoryChapter { Id = StoryAuthoring.NewId(), Name = "Second chapter" };
        scoped.Story!.Chapters.Add(chapterTwo);
        var standalone = NativeQuestAuthoring.Create();
        scoped.Quests.Add(standalone);
        check(QuestStoryFlow.Quests(scoped, "").Single() == standalone, "Non-story editor excludes every valid chapter quest");
        QuestStoryFlow.AssignQuest(scoped, (string)secondQuest.Id!, chapterTwo.Id);
        check(
            QuestStoryFlow.Quests(scoped, firstChapter.Id).Count() == 1 && QuestStoryFlow.Quests(scoped, chapterTwo.Id).Count() == 1,
            "Each chapter editor includes only its assigned quests"
        );
        var scopedQuest = QuestStoryFlow.Quests(scoped, firstChapter.Id).Single();
        scoped.Story.Quests.First(m => m.QuestId == (string)scopedQuest.Id!).Main = false;
        var scopedCopy = QuestStoryFlow.DuplicateQuest(scoped, scopedQuest);
        check(
            QuestStoryFlow.ChapterForQuest(scoped, (string)scopedCopy.Id!) == firstChapter.Id && !scoped.Story.Quests.Last().Main,
            "Duplicating a chapter quest retains chapter and optional status"
        );
        check(
            scoped.Story.Quests.Last().StatusNotes["Success"].Single() == flowNote.Id,
            "Duplicated quest retains external journal references"
        );
        check(
            QuestStoryFlow.DeleteQuest(scoped, scopedCopy).Count == 0 && !scoped.Story.Quests.Any(m => m.QuestId == (string)scopedCopy.Id!),
            "Deleting an unreferenced chapter quest also removes its own membership"
        );
        var scopedDialog = StoryAuthoring.AddConversation(scoped, "54cb50c76803fa8b248b4571", false, (string)scopedQuest.Id!);
        var protectedSource = JsonConvert.SerializeObject(scoped);
        check(
            QuestStoryFlow.DeleteQuest(scoped, scopedQuest).Count > 0 && JsonConvert.SerializeObject(scoped) == protectedSource,
            "Referenced chapter quest deletion is blocked without changing any draft data"
        );
        var orphan = scoped.Story.Quests.First(m => m.QuestId == (string)scopedQuest.Id!);
        orphan.ChapterId = "missing";
        check(QuestStoryFlow.Quests(scoped, "").Contains(scopedQuest), "Imported orphan story membership remains accessible for repair");
        QuestStoryFlow.AssignQuest(scoped, orphan.QuestId, firstChapter.Id);
        check(
            QuestStoryFlow.ChapterForQuest(scoped, orphan.QuestId) == firstChapter.Id
                && scoped.Story.Quests.Count(m => m.QuestId == orphan.QuestId) == 1,
            "Repair reuses existing membership without creating duplicates"
        );
        var standaloneCopy = QuestStoryFlow.DuplicateQuest(scoped, standalone);
        check(
            QuestStoryFlow.Quests(scoped, "").Contains(standaloneCopy)
                && !scoped.Story.Quests.Any(m => m.QuestId == (string)standaloneCopy.Id!),
            "Non-story duplication never creates story membership"
        );
        var scopedRoundtrip = SeasonCompiler.Copy(scoped);
        check(
            QuestStoryFlow.Quests(scopedRoundtrip, chapterTwo.Id).Count() == 1 && QuestStoryFlow.Quests(scopedRoundtrip, "").Count() == 2,
            "Chapter and non-story separation survives draft roundtrip"
        );
        var removal = SeasonCompiler.Copy(flow);
        var removalChapter = removal.Story!.Chapters.Single();
        var removalBefore = JsonConvert.SerializeObject(removal);
        check(
            ChapterDeletion.Check(removal, removalChapter.Id, null).Count == 0 && JsonConvert.SerializeObject(removal) == removalBefore,
            "Chapter deletion preview permits its own quests and notes without mutating the draft"
        );
        check(
            ChapterDeletion.Delete(removal, removalChapter.Id, null).Count == 0
                && removal.Story.Chapters.Count == 0
                && removal.Quests.Count == 0
                && removal.Story.Quests.Count == 0
                && removal.Story.Notes.Count == 0,
            "Deleting chapter contents removes three linked collections together"
        );
        removal = SeasonCompiler.Copy(flow);
        removalChapter = removal.Story!.Chapters.Single();
        var retainedDialog = StoryAuthoring.AddConversation(removal, "54cb50c76803fa8b248b4571", false, removal.Quests[0].Id);
        removalBefore = JsonConvert.SerializeObject(removal);
        var blockedChapter = ChapterDeletion.Delete(removal, removalChapter.Id, null);
        check(
            blockedChapter.Any(u => u.Navigation == "Story/" + retainedDialog.Id) && JsonConvert.SerializeObject(removal) == removalBefore,
            "External conversations block chapter deletion with an actionable link and complete rollback"
        );
        var destinationChapter = new StoryChapter { Id = StoryAuthoring.NewId(), Name = "Keep contents here" };
        removal.Story.Chapters.Add(destinationChapter);
        check(
            ChapterDeletion.Delete(removal, removalChapter.Id, destinationChapter.Id).Count == 0
                && removal.Quests.Count == 2
                && removal.Story.Quests.All(q => q.ChapterId == destinationChapter.Id)
                && removal.Story.Notes.All(n => n.ChapterId == destinationChapter.Id)
                && removal.Story.Dialogs.Single().Id == retainedDialog.Id,
            "Moving chapter contents preserves native quests, note identities and conversations"
        );
        removalBefore = JsonConvert.SerializeObject(removal);
        try
        {
            ChapterDeletion.Delete(removal, destinationChapter.Id, destinationChapter.Id);
        }
        catch (ArgumentException) { }

        try
        {
            ChapterDeletion.Delete(removal, destinationChapter.Id, "missing");
        }
        catch (ArgumentException) { }

        check(JsonConvert.SerializeObject(removal) == removalBefore, "Invalid chapter destinations cannot partially remove content");
        var emptyChapter = new StoryChapter { Id = StoryAuthoring.NewId() };
        removal.Story.Chapters.Add(emptyChapter);
        check(
            ChapterDeletion.Delete(removal, emptyChapter.Id, null).Count == 0 && removal.Story.Chapters.Count == 1,
            "Empty chapter deletion preserves unrelated chapters"
        );
        var referencedNote = removal.Story.Notes.Single();
        retainedDialog
            .Lines[0]
            .Actions.Add(
                new StoryAction
                {
                    Id = StoryAuthoring.NewId(),
                    Type = StoryActionType.DiaryNote,
                    Target = referencedNote.Id,
                }
            );
        check(
            ChapterDeletion.Check(removal, destinationChapter.Id, null).Any(u => u.Path.Contains("Actions")),
            "Chapter deletion also protects notes used by external dialogue actions"
        );
        var season = new SeasonDefinition { Id = StoryAuthoring.NewId(), Story = new() };
        var chapter = new StoryChapter { Id = StoryAuthoring.NewId(), Name = "A new beginning" };
        season.Story.Chapters.Add(chapter);
        var quest = NativeQuestAuthoring.Create();
        season.Quests.Add(quest);
        var questId = (string)quest.Id!;
        var note = new StoryNote
        {
            Id = StoryAuthoring.NewId(),
            ChapterId = chapter.Id,
            Text = "The job is done.",
        };
        season.Story.Notes.Add(note);
        season.Story.Quests.Add(
            new()
            {
                QuestId = questId,
                ChapterId = chapter.Id,
                StatusNotes = new() { ["Success"] = [note.Id] },
            }
        );
        var dialog = StoryAuthoring.AddConversation(season, "54cb50c76803fa8b248b4571", true, questId);
        var validation = new SeasonValidationResult();
        StoryValidator.Validate(season, validation);
        check(validation.CanPublish, "Graphical templates produce structurally valid story content");
        check(StoryAuthoring.Uses(season, chapter).Count == 2, "Chapter deletion identifies quest and note references");
        check(StoryAuthoring.Uses(season, note).Count == 1, "Note deletion identifies status-trigger reference");
        check(StoryAuthoring.Uses(season, season.Story.Variables[0]).Count > 0, "Phase variable deletion is protected");
        var duplicate = (StoryDialog)StoryAuthoring.Duplicate(dialog);
        check(
            duplicate.Id != dialog.Id
                && duplicate.Lines[0].Id != dialog.Lines[0].Id
                && duplicate.Lines[0].Actions[0].Id != dialog.Lines[0].Actions[0].Id,
            "Nested story identities remap when duplicated"
        );
        check(
            duplicate.TraderId == dialog.TraderId && duplicate.MainVariable == dialog.MainVariable,
            "Duplication preserves references outside copied subtree"
        );
        var duplicateQuest = (NativeQuest)StoryAuthoring.Duplicate(quest);
        check(
            NativeQuestAuthoring.QuestName(duplicateQuest) == "New quest" && (string)duplicateQuest.Id! != questId,
            "Native quest duplication preserves remapped English names"
        );
        check(duplicateQuest.English()[(string)duplicateQuest.Id! + " name"] != null, "Native localization dictionary keys remap");
        JsonConvert.PopulateObject("{customImportedField:{preserve:12}}", quest);
        NativeQuestAuthoring.QuestText(quest, "name", "An authored quest");
        check((int?)JObject.FromObject(quest)["customImportedField"]?["preserve"] == 12, "Simplified quest edits retain imported fields");
        check(
            !StoryAuthoring.Visible(new StoryAction { Type = StoryActionType.QuitAction }, "QuestId"),
            "Actions expose only relevant fields"
        );
        check(
            StoryAuthoring.ReferenceKind(new StoryAction { Type = StoryActionType.SetVariable }, "Target") == "variables",
            "Variable actions use a variable picker"
        );
        check(
            !StoryAuthoring.Options(new StoryCondition(), "Type")!.Contains("ServiceAvailable"),
            "Unsupported paid service condition is not offered"
        );
        check(
            !NativeQuestAuthoring.ConditionKinds(true, false).Contains("Kills")
                && NativeQuestAuthoring.ConditionKinds(true, true).Contains("Kills"),
            "Counter filters are offered only within counters"
        );
        var counter = NativeQuestAuthoring.Condition("CounterCreator");
        counter.Counter!.Conditions.Add(NativeQuestAuthoring.Condition("Kills"));
        check(
            StoryQuestCompatibility.Supports(counter.Counter!.Conditions[0], true),
            "Graphical counter filter has correct native nesting"
        );
        var handover = quest.Conditions.AvailableForFinish[0];
        var objectiveId = (string)handover.Id!;
        const string item = "5449016a4bdc2d6f028b456f";
        handover.Target = new StringTargets(new[] { item });
        handover.Value = 2;
        var source = JsonConvert.SerializeObject(season);
        var facts = new StoryFacts
        {
            Level = 1,
            Items = new() { [item] = 3 },
            HandoverItems = new() { [objectiveId] = 3 },
        };
        var run = new StoryRehearsal(season, facts, 42);
        run.Start(season.Story.EntryPoints[0].Id);
        check(run.Error.Length == 0 && run.Replies.Count == 2, "Rehearsal advances the NPC phase and offers branching replies");
        run.Select(dialog.Lines[1].Id);
        check(run.Facts.QuestStatuses[questId] == "Started", "Rehearsal simulates owned quest acceptance");
        dialog
            .Lines[3]
            .Actions.Insert(
                0,
                new()
                {
                    Id = StoryAuthoring.NewId(),
                    Type = StoryActionType.HandoverItem,
                    QuestId = questId,
                    ConditionId = objectiveId,
                }
            );
        dialog
            .Lines[3]
            .Actions.Insert(
                1,
                new()
                {
                    Id = StoryAuthoring.NewId(),
                    Type = StoryActionType.PlayerReward,
                    QuestId = questId,
                }
            );
        check(run.Stale(season), "Draft edits invalidate an existing rehearsal");
        run = new(season, facts, 42);
        run.Start(season.Story.EntryPoints[0].Id);
        run.Select(dialog.Lines[1].Id);
        run.Select(dialog.Lines[3].Id);
        check(
            run.Facts.Items[item] == 1 && run.Facts.ConditionCounters[objectiveId] == 2,
            "Simple handover consumes only simulated required quantities"
        );
        check(
            run.Facts.QuestStatuses[questId] == "Success" && run.State.Notes.ContainsKey(note.Id),
            "Finishing a simulated quest reveals its status note"
        );
        check(StoryRules.ChapterComplete(chapter, run.Definition, run.Facts), "Required quest completes rehearsal chapter");
        check(facts.Items[item] == 3 && facts.QuestStatuses.Count == 0, "Rehearsal copies caller facts");
        var bad = SeasonCompiler.Copy(season);
        bad.Story!.Dialogs[0].Lines[0].Actions.Clear();
        var broken = new StoryRehearsal(bad, facts, 42);
        var before = JsonConvert.SerializeObject(broken.State);
        broken.Start(bad.Story.EntryPoints[0].Id);
        check(
            broken.Error.Contains("looping") && JsonConvert.SerializeObject(broken.State) == before,
            "Automatic loop rolls back entire rehearsal step"
        );
        handover.OnlyFoundInRaid = true;
        var manual = new StoryRehearsal(season, facts, 42);
        manual.Start(season.Story.EntryPoints[0].Id);
        manual.Select(dialog.Lines[1].Id);
        before = JsonConvert.SerializeObject(manual.State);
        manual.Select(dialog.Lines[3].Id);
        check(
            manual.Pending != null && JsonConvert.SerializeObject(manual.State) == before,
            "Filtered handover pauses without committing partial actions"
        );
        manual.Resume(false);
        check(manual.Pending == null && JsonConvert.SerializeObject(manual.State) == before, "Manual failure preserves original state");
        manual.Select(dialog.Lines[3].Id);
        manual.Resume(true);
        check(
            manual.Pending == null && manual.Facts.QuestStatuses[questId] == "Success",
            "Manual success replays and commits the paused step"
        );
        var raid = new StoryRaidBinding
        {
            Id = StoryAuthoring.NewId(),
            Location = "woods",
            ObjectPath = "scene:/Test/Trigger",
            Actions =
            [
                new()
                {
                    Id = StoryAuthoring.NewId(),
                    Type = StoryActionType.DiaryNote,
                    Target = note.Id,
                },
            ],
        };
        season.Story.RaidBindings.Add(raid);
        var raidRun = new StoryRehearsal(
            season,
            new()
            {
                InRaid = true,
                Location = "woods",
                Scene = "scene",
            },
            7
        );
        raidRun.Trigger(raid.Id);
        check(!raidRun.State.Notes.ContainsKey(note.Id), "Survival-dependent binding defers its actions");
        raidRun.FinishRaid(false);
        check(!raidRun.State.Notes.ContainsKey(note.Id), "Death discards deferred actions");
        raidRun = new(
            season,
            new()
            {
                InRaid = true,
                Location = "woods",
                Scene = "scene",
            },
            7
        );
        raidRun.Trigger(raid.Id);
        raidRun.FinishRaid(true);
        check(raidRun.State.Notes.ContainsKey(note.Id), "Survival commits deferred actions");
        var roundtrip = JsonConvert.DeserializeObject<SeasonDefinition>(JsonConvert.SerializeObject(season))!;
        check(SeasonCompiler.Texts(roundtrip)[note.Id + " text"] == note.Text, "Story text is available to localization after roundtrip");
        check(
            SeasonCompiler.GameplayIdentity(roundtrip) == SeasonCompiler.GameplayIdentity(season),
            "Editor-authored story roundtrip preserves gameplay identity"
        );
        check(
            source
                == JsonConvert.SerializeObject(
                    new StoryRehearsal(JsonConvert.DeserializeObject<SeasonDefinition>(source)!, facts, 42).Season
                ),
            "Starting rehearsal does not mutate authored content"
        );
    }
}
