using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SeasonalPerks.Server.Seasons;
using SeasonalPerks.Shared.Seasons;
using SeasonalPerks.Shared.Story;

namespace SeasonalPerks.Tests;

internal static class PlayableStorySeason
{
    internal static void Build(string installedMod, string output)
    {
        output = Path.GetFullPath(output);
        if (Directory.Exists(output))
        {
            throw new IOException("Choose a new output folder; existing test seasons are preserved.");
        }

        var workspace = Path.Combine(output, "authoring");
        foreach (var source in Directory.GetFiles(Path.Combine(installedMod, "data"), "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(workspace, "data", Path.GetRelativePath(Path.Combine(installedMod, "data"), source));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target);
        }
        var store = new SeasonRepository(workspace);
        var draft = store.Create(false);
        var season = draft.Definition;
        // A blank season's document normally clones legacy content. Keep this pack self-contained.
        season.Items.Single().CloneFrom = "590c645c86f77412b01304d9";
        season.Collection.DocumentsPerRaid = 0;
        season.Collection.MapCounts.Clear();
        season.Name = "Story Sandbox";
        season.Description =
            "A short Prapor delivery for testing the story journal and trader dialogue. Start a separate character, visit Prapor, and hand over one aseptic bandage. Two bandages are included in the starter stash.";
        season.Author = "Local test";
        season.Rules.StartingPoints = 1;
        var perk = SeasonRepository.NewId();
        season.Perks.Personal.Add(
            new()
            {
                Id = perk,
                Type = "personal",
                Points = -1,
                ImageUrl = season.UniversalImage,
                Effects = [new() { EffectId = "energy_drain_multiplicator", Multiplier = .8 }],
            }
        );
        season.Locales["en"][perk + " name"] = "Enduring";
        season.Locales["en"][perk + " description"] = "Energy drains 20% slower. Included for this test season's character creation.";
        const string bandage = "544fb25a4bdc2dfb738b4567";
        const string trader = "54cb50c76803fa8b248b4571";
        const string roubles = "5449016a4bdc2d6f028b456f";
        season.Starting.Usec.Items.Add(new() { Template = bandage, Count = 2 });
        season.Starting.Bear.Items.Add(new() { Template = bandage, Count = 2 });
        var quest = SeasonRepository.NewId();
        var objective = SeasonRepository.NewId();
        var chapter = SeasonRepository.NewId();
        var briefing = SeasonRepository.NewId();
        var acceptedNote = SeasonRepository.NewId();
        var finishedNote = SeasonRepository.NewId();
        var phase = SeasonRepository.NewId();
        var dialog = SeasonRepository.NewId();
        var entry = SeasonRepository.NewId();
        var rewardItem = SeasonRepository.NewId();
        var native = new JObject
        {
            ["_id"] = quest,
            ["QuestName"] = "Field dressing",
            ["name"] = quest + " name",
            ["description"] = quest + " description",
            ["traderId"] = trader,
            ["location"] = "any",
            ["image"] = "/files/quest/icon/596b36c586f77450d6045ad2.jpg",
            ["type"] = "PickUp",
            ["side"] = "Pmc",
            ["canShowNotificationsInGame"] = true,
            ["restartable"] = false,
            ["conditions"] = new JObject
            {
                ["AvailableForStart"] = new JArray(
                    new JObject
                    {
                        ["id"] = SeasonRepository.NewId(),
                        ["conditionType"] = "Level",
                        ["value"] = 1,
                        ["compareMethod"] = ">=",
                        ["dynamicLocale"] = false,
                        ["visibilityConditions"] = new JArray(),
                    }
                ),
                ["AvailableForFinish"] = new JArray(
                    new JObject
                    {
                        ["id"] = objective,
                        ["conditionType"] = "HandoverItem",
                        ["target"] = new JArray(bandage),
                        ["value"] = 1,
                        ["onlyFoundInRaid"] = false,
                        ["minDurability"] = 0,
                        ["maxDurability"] = 100,
                        ["dynamicLocale"] = false,
                        ["visibilityConditions"] = new JArray(),
                    }
                ),
                ["Fail"] = new JArray(),
            },
            ["rewards"] = new JObject
            {
                ["Started"] = new JArray(),
                ["Fail"] = new JArray(),
                ["Success"] = new JArray(
                    new JObject
                    {
                        ["id"] = SeasonRepository.NewId(),
                        ["type"] = "Experience",
                        ["value"] = 250,
                    },
                    new JObject
                    {
                        ["id"] = SeasonRepository.NewId(),
                        ["type"] = "Item",
                        ["target"] = rewardItem,
                        ["value"] = 5000,
                        ["items"] = new JArray(
                            new JObject
                            {
                                ["_id"] = rewardItem,
                                ["_tpl"] = roubles,
                                ["upd"] = new JObject { ["StackObjectsCount"] = 5000 },
                            }
                        ),
                    }
                ),
            },
            ["localization"] = new JObject
            {
                ["en"] = new JObject
                {
                    [quest + " name"] = "Field dressing",
                    [quest + " description"] =
                        "Prapor needs one aseptic bandage for a delivery. Visit him to accept, hand over the bandage, and collect your reward.",
                    [objective] = "Hand over one aseptic bandage to Prapor",
                },
            },
        };
        foreach (
            var field in new[]
            {
                "startedMessageText",
                "successMessageText",
                "failMessageText",
                "acceptPlayerMessage",
                "completePlayerMessage",
                "declinePlayerMessage",
            }
        )
        {
            native[field] = quest + " " + field;
            native["localization"]!["en"]![quest + " " + field] =
                field == "successMessageText"
                    ? "The delivery is complete. Here is your payment."
                    : "Field dressing: deliver one aseptic bandage.";
        }
        season.Quests.Add(native.ToObject<NativeQuest>()!);
        StoryCondition Phase(int value)
        {
            return new()
            {
                Type = "VariableValue",
                Target = phase,
                Operator = "==",
                Value = value,
            };
        }

        StoryCondition Status(params string[] values)
        {
            return new()
            {
                Type = "QuestStatus",
                Target = quest,
                Status = values.ToList(),
            };
        }

        StoryCondition All(params StoryCondition[] values)
        {
            return new() { Conditions = values.ToList() };
        }

        StoryCondition Done()
        {
            return new() { Type = "CompleteCondition", Target = objective };
        }

        StoryAction Set(int value)
        {
            return new()
            {
                Id = SeasonRepository.NewId(),
                Type = StoryActionType.SetVariable,
                Target = phase,
                Scope = StoryVariableScope.Dialogue,
                Value = value,
            };
        }

        StoryAction Native(StoryActionType type)
        {
            return new()
            {
                Id = SeasonRepository.NewId(),
                Type = type,
                QuestId = quest,
                ConditionId = type == StoryActionType.HandoverItem ? objective : "",
            };
        }

        StoryDialogLine Line(string side, string text, StoryCondition trigger, params StoryAction[] actions)
        {
            return new()
            {
                Id = SeasonRepository.NewId(),
                Side = side,
                Text = text,
                Trigger = trigger,
                Actions = actions.ToList(),
            };
        }

        var accept = Line("Player", "I'll make the delivery.", Phase(1), Native(StoryActionType.AcceptQuest), Set(10));
        var handover = Line(
            "Player",
            "Hand over one aseptic bandage.",
            All(Phase(2), new() { Type = "HasItemForHandover", Target = objective }),
            Native(StoryActionType.HandoverItem),
            Set(3)
        );
        handover.Confirmation = "Hand over one aseptic bandage from this character's inventory?";
        var finish = Line("Player", "Collect the reward.", Phase(4), Native(StoryActionType.FinishQuest), Set(5));
        var leave = Line(
            "Player",
            "I'll be going.",
            new(),
            new StoryAction() { Id = SeasonRepository.NewId(), Type = StoryActionType.QuitAction }
        );
        season.Story = new()
        {
            Chapters = [new() { Id = chapter, Name = "Field dressing" }],
            Quests =
            [
                new()
                {
                    QuestId = quest,
                    ChapterId = chapter,
                    StatusNotes = new() { ["Started"] = [acceptedNote], ["Success"] = [finishedNote] },
                },
            ],
            Notes =
            [
                new()
                {
                    Id = briefing,
                    ChapterId = chapter,
                    Text = "Prapor needs a bandage for a supply delivery. I can take the job during a visit.",
                },
                new()
                {
                    Id = acceptedNote,
                    ChapterId = chapter,
                    Text =
                        "I agreed to bring Prapor one aseptic bandage. It does not need to be found in raid. My starter stash has two; I should keep one and hand over the other.",
                },
                new()
                {
                    Id = finishedNote,
                    ChapterId = chapter,
                    Text =
                        "Prapor accepted the bandage. The delivery is complete: 250 XP and 5,000 roubles. His payment can be collected in Messenger.",
                },
            ],
            Variables = [new() { Id = phase, Scope = StoryVariableScope.Dialogue }],
            EntryPoints =
            [
                new()
                {
                    Id = entry,
                    DialogId = dialog,
                    TraderId = trader,
                    Kind = "InLobby",
                },
            ],
            Dialogs =
            [
                new()
                {
                    Id = dialog,
                    TraderId = trader,
                    MainVariable = phase,
                    Lines =
                    [
                        Line(
                            "Npc",
                            "Got a simple delivery for you. Bring me one aseptic bandage and I'll pay you for the trouble.",
                            All(Phase(0), Status("Locked", "AvailableForStart")),
                            new()
                            {
                                Id = SeasonRepository.NewId(),
                                Type = StoryActionType.DiaryNote,
                                Target = briefing,
                            },
                            Set(1)
                        ),
                        accept,
                        Line("Npc", "Good. One aseptic bandage. You can use one from your stash; no raid needed.", Phase(10), Set(2)),
                        Line(
                            "Npc",
                            "Still need that bandage. Have you got one for me?",
                            All(Phase(0), Status("Started", "AvailableForFinish"), new() { Type = "Not", Conditions = [Done()] }),
                            Set(2)
                        ),
                        handover,
                        Line("Player", "Where can I get the bandage?", Phase(2), Set(20)),
                        Line(
                            "Npc",
                            "Check your starter stash. If you've used those, buy an aseptic bandage from Therapist.",
                            Phase(20),
                            Set(2)
                        ),
                        Line("Npc", "That's the one. Delivery received. Your payment is ready.", Phase(3), Set(4)),
                        Line(
                            "Npc",
                            "The bandage is accounted for. Let's settle your payment.",
                            All(Phase(0), Status("Started", "AvailableForFinish"), Done()),
                            Set(4)
                        ),
                        finish,
                        Line("Npc", "All settled. Check your messages for the roubles. Good work.", Phase(5), Set(6)),
                        Line(
                            "Npc",
                            "That delivery is already settled. Check your journal and messages if you need the details.",
                            All(Phase(0), Status("Success")),
                            Set(6)
                        ),
                        leave,
                    ],
                },
            ],
        };
        var installed = new SeasonRepository(installedMod);
        foreach (var asset in SeasonCompiler.Assets(season).Where(SeasonValidator.IsId))
        {
            var source = installed.AssetPath(asset) ?? throw new FileNotFoundException("Missing base artwork: " + asset);
            File.Copy(source, Path.Combine(workspace, "creator", "assets", asset + ".png"), true);
        }
        draft = store.Save(draft);
        var validation = SeasonValidator.Validate(season);
        if (!validation.CanPublish)
        {
            throw new InvalidDataException(JsonConvert.SerializeObject(validation, Formatting.Indented));
        }

        var key = store.Publish(draft, validation);
        var bytes = store.Export(key);
        var roundtrip = store.Import(bytes);
        if (SeasonRepository.GameplayHash(roundtrip.Definition) != SeasonRepository.GameplayHash(season))
        {
            throw new InvalidDataException("Story test season failed round-trip validation.");
        }

        File.WriteAllBytes(Path.Combine(output, "Story-Sandbox.zip"), bytes);
        File.WriteAllText(
            Path.Combine(output, "test-story.json"),
            JsonConvert.SerializeObject(
                new
                {
                    SeasonId = season.Id,
                    PackKey = key,
                    QuestId = quest,
                    ObjectiveId = objective,
                    EntryId = entry,
                    ChapterId = chapter,
                    PerkId = perk,
                    Bandage = bandage,
                    AcceptId = accept.Id,
                    HandoverId = handover.Id,
                    FinishId = finish.Id,
                    LeaveId = leave.Id,
                },
                Formatting.Indented
            )
        );
        Console.WriteLine("Validated test season: " + output);
    }
}
