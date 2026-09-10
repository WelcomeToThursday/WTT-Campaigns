using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using WTT.Campaigns.Server.Seasons;
using WTT.Campaigns.Shared.Seasons;
using WTT.Campaigns.Shared.Story;

namespace WTT.Campaigns.Tests;

internal static class StoryFixture
{
    internal static void Add(SeasonDefinition season, string questId, string handover, string nextQuest, string nextObjective)
    {
        string Id()
        {
            return SeasonRepository.NewId();
        }
        var chapter = Id();
        var second = Id();
        var variable = Id();
        var dialog = Id();
        var note = Id();
        var entry = Id();
        var cinematic = Id();
        var trader = "54cb50c76803fa8b248b4571";
        var item = season.Documents[0].ItemId;
        StoryCondition Phase(int value)
        {
            return new()
            {
                Type = "VariableValue",
                Target = variable,
                Value = value,
                Operator = "==",
            };
        }
        StoryAction Set(int value)
        {
            return new()
            {
                Id = Id(),
                Type = StoryActionType.SetVariable,
                Target = variable,
                Value = value,
            };
        }
        StoryDialogLine Line(string text, string side, int phase, params StoryAction[] actions)
        {
            return new()
            {
                Id = Id(),
                Text = text,
                Side = side,
                Trigger = Phase(phase),
                Actions = actions.ToList(),
            };
        }
        var lines = new List<StoryDialogLine>
        {
            Line("This is a synthetic story-system test.", "Npc", 0, Set(1)),
            Line(
                "Accept the test delivery.",
                "Player",
                1,
                new()
                {
                    Id = Id(),
                    Type = StoryActionType.AcceptQuest,
                    QuestId = questId,
                },
                Set(2)
            ),
            Line(
                "Test an invalid completion.",
                "Player",
                1,
                Set(99),
                new()
                {
                    Id = Id(),
                    Type = StoryActionType.FinishQuest,
                    QuestId = questId,
                }
            ),
            Line(
                "Bring one research note.",
                "Npc",
                2,
                new()
                {
                    Id = Id(),
                    Type = StoryActionType.DiaryNote,
                    Target = note,
                },
                Set(3)
            ),
            Line(
                "Hand over one note.",
                "Player",
                3,
                new()
                {
                    Id = Id(),
                    Type = StoryActionType.HandoverItem,
                    QuestId = questId,
                    ConditionId = handover,
                },
                new()
                {
                    Id = Id(),
                    Type = StoryActionType.FinishQuest,
                    QuestId = questId,
                },
                Set(4)
            ),
            Line("Delivery recorded. The second chapter is now available.", "Npc", 4, Set(5)),
            Line("Leave.", "Player", 5, new StoryAction { Id = Id(), Type = StoryActionType.QuitAction }),
        };
        season.Name = "Story framework acceptance";
        season.Story = new StoryDefinition
        {
            Chapters =
            [
                new()
                {
                    Id = chapter,
                    Name = "Synthetic delivery",
                    Image = season.UniversalImage,
                    Icon = season.UniversalImage,
                },
                new()
                {
                    Id = second,
                    Name = "Synthetic discovery",
                    Order = 1,
                    Image = season.UniversalImage,
                    Icon = season.UniversalImage,
                },
            ],
            Quests =
            [
                new() { QuestId = questId, ChapterId = chapter },
                new()
                {
                    QuestId = nextQuest,
                    ChapterId = second,
                    AutoStart = true,
                    AutoComplete = true,
                },
            ],
            Notes =
            [
                new()
                {
                    Id = note,
                    ChapterId = chapter,
                    Text = "The synthetic trader asked for a research note.",
                    Links =
                    [
                        new()
                        {
                            Id = Id(),
                            Target = item,
                            Kind = "Item",
                        },
                    ],
                },
            ],
            Variables = [new() { Id = variable }],
            Dialogs =
            [
                new()
                {
                    Id = dialog,
                    TraderId = trader,
                    MainVariable = variable,
                    Lines = lines,
                },
            ],
            EntryPoints =
            [
                new()
                {
                    Id = entry,
                    DialogId = dialog,
                    TraderId = trader,
                },
            ],
            RaidBindings =
            [
                new()
                {
                    Id = Id(),
                    Location = "factory4_day",
                    Kind = "Collectible",
                    ItemId = item,
                    PersistOnDeath = true,
                },
                new()
                {
                    Id = Id(),
                    Location = "factory4_day",
                    Kind = "Trigger",
                    ObjectPath = "factory4_day:/StoryTest/Extraction",
                    PersistOnDeath = false,
                },
                new()
                {
                    Id = Id(),
                    Location = "factory4_day",
                    Kind = "Cinematic",
                    ObjectPath = "factory4_day:/StoryTest/Cinematic",
                    MediaId = cinematic,
                    PersistOnDeath = true,
                },
            ],
            Media =
            [
                new()
                {
                    Id = cinematic,
                    Kind = "Cinematic",
                    Bundle = "examples/story-test.bundle",
                    Asset = "assets/story-test.prefab",
                    Sha256 = Convert
                        .ToHexString(SHA256.HashData(File.ReadAllBytes("Client/Resources/StoryMedia/examples/story-test.bundle")))
                        .ToLowerInvariant(),
                },
            ],
        };
        var next = season.Quests.Single(q => (string?)q.Id == nextQuest);
        var condition = next.Conditions.AvailableForFinish[0];
        condition.ConditionType = "CompletableItem";
        condition.Target = item;
        next.English()[nextObjective] = "Record a synthetic collectible in a raid";
        next.Rewards["Success"]
            .Add(
                new NativeReward
                {
                    Id = Id(),
                    Type = "Experience",
                    Value = 75,
                    Index = 0,
                }
            );
        var first = season.Quests.Single(q => (string?)q.Id == questId);
        first
            .Rewards["Success"]
            .Add(
                new NativeReward
                {
                    Id = Id(),
                    Type = "Experience",
                    Value = 100,
                    Index = 0,
                }
            );
    }
}
