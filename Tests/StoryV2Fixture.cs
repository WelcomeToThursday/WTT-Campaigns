using WTT.Campaigns.Server.Web.Authoring;
using WTT.Campaigns.Shared.Seasons;
using WTT.Campaigns.Shared.Story;

namespace WTT.Campaigns.Tests;

internal static class StoryV2Fixture
{
    internal static void Add(SeasonDefinition season)
    {
        var story = season.Story!;
        var trader = story.Dialogs[0].TraderId;
        string Id()
        {
            return StoryAuthoring.NewId();
        }

        var phase = new StoryVariable { Id = Id(), Scope = StoryVariableScope.Dialogue };
        story.Variables.Add(phase);
        var actions = new List<StoryAction>();
        for (var i = 0; i < 2; i++)
        {
            var quest = (NativeQuest)StoryAuthoring.Duplicate(season.Quests[0]);
            quest.QuestName = "Automatic handover " + (i + 1);
            season.Quests.Add(quest);
            story.Quests.Add(
                new()
                {
                    QuestId = quest.Id!,
                    ChapterId = story.Chapters[0].Id,
                    Main = false,
                }
            );
            actions.Add(
                new()
                {
                    Id = Id(),
                    Type = StoryActionType.AcceptQuest,
                    QuestId = quest.Id!,
                }
            );
            actions.Add(
                new()
                {
                    Id = Id(),
                    Type = StoryActionType.HandoverItem,
                    QuestId = quest.Id!,
                    ConditionId = quest.Conditions.AvailableForFinish[0].Id,
                }
            );
            actions.Add(
                new()
                {
                    Id = Id(),
                    Type = StoryActionType.FinishQuest,
                    QuestId = quest.Id!,
                }
            );
        }
        var flag = Id();
        actions.Add(
            new()
            {
                Id = Id(),
                Type = StoryActionType.CompleteItem,
                Target = flag,
            }
        );
        actions.Add(new() { Id = Id(), Type = StoryActionType.QuitAction });
        var dialog = new StoryDialog
        {
            Id = Id(),
            TraderId = trader,
            MainVariable = phase.Id,
            Lines =
            [
                new()
                {
                    Id = Id(),
                    Side = "Npc",
                    Text = "Both automatic deliveries are complete.",
                    Actions = actions,
                },
            ],
        };
        story.Dialogs.Add(dialog);
        story.EntryPoints.Add(
            new()
            {
                Id = Id(),
                DialogId = dialog.Id,
                TraderId = trader,
                Scene = "V2Lobby",
                Condition = new() { Type = "CurrentTrader", Target = trader },
            }
        );
        var raidPhase = new StoryVariable { Id = Id(), Scope = StoryVariableScope.Dialogue };
        story.Variables.Add(raidPhase);
        var followup = new StoryDialog
        {
            Id = Id(),
            TraderId = trader,
            MainVariable = raidPhase.Id,
            Lines =
            [
                new()
                {
                    Id = Id(),
                    Side = "Npc",
                    Text = "The cinematic follow-up is visible.",
                    Actions =
                    [
                        new()
                        {
                            Id = Id(),
                            Type = StoryActionType.CompleteItem,
                            Target = Id(),
                        },
                        new()
                        {
                            Id = Id(),
                            Type = StoryActionType.StartCinematic,
                            Target = story.Media[0].Id,
                        },
                        new() { Id = Id(), Type = StoryActionType.QuitAction },
                    ],
                },
            ],
        };
        story.Dialogs.Add(followup);
        var entry = new StoryEntryPoint
        {
            Id = Id(),
            DialogId = followup.Id,
            TraderId = trader,
            Kind = "ViaRadio",
            Scene = "factory4_day",
        };
        story.EntryPoints.Add(entry);
        story.RaidBindings.Single(b => b.Kind == "Cinematic").EntryPointId = entry.Id;
        foreach (var kind in new[] { "Image", "Audio", "Video", "Cinematic" })
        {
            var media = new StoryMedia
            {
                Id = Id(),
                Kind = kind,
                Bundle = story.Media[0].Bundle,
                Asset = "assets/test-" + kind,
                Sha256 = story.Media[0].Sha256,
            };
            story.Media.Add(media);
            story.RaidBindings.Add(
                new()
                {
                    Id = Id(),
                    Kind = "Interact",
                    Location = "factory4_day",
                    ObjectPath = "factory4_day:/V2/" + kind,
                    MediaId = media.Id,
                    PersistOnDeath = false,
                    Condition = new()
                    {
                        Conditions =
                        [
                            new() { Type = "HasItem", Target = season.Documents[0].ItemId },
                            new() { Type = "QuestConditionStatus", Target = season.Quests[0].Conditions.AvailableForFinish[0].Id },
                        ],
                    },
                }
            );
        }
    }
}
