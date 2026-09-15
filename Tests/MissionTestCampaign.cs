using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WTT.Campaigns.Server.Seasons;
using WTT.Campaigns.Shared.Missions;
using WTT.Campaigns.Shared.Native;
using WTT.Campaigns.Shared.Seasons;
using WTT.Campaigns.Shared.Spatial;
using WTT.Campaigns.Shared.Story;

namespace WTT.Campaigns.Tests;

/// <summary>File-only preparation of the user's existing test draft; never starts a runtime.</summary>
internal static class MissionTestCampaign
{
    internal static void Prepare(string source, string layoutId, string output)
    {
        if (Path.GetFullPath(source).Equals(Path.GetFullPath(output), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Stage the prepared draft separately before installation.");
        if (File.Exists(output))
            throw new IOException("Choose a new staging file; existing output is preserved.");
        var sourceBytes = File.ReadAllBytes(source);
        var draft =
            JsonConvert.DeserializeObject<DraftEnvelope>(Encoding.UTF8.GetString(sourceBytes).TrimStart('\uFEFF'))
            ?? throw new InvalidOperationException("The source draft is empty.");
        var prepared = AddMission(draft.Definition, layoutId);
        var validation = SeasonValidator.Validate(prepared);
        if (!validation.CanPublish)
            throw new InvalidOperationException(
                string.Join(Environment.NewLine, validation.Issues.Where(i => i.Severity == "error").Select(i => i.Path + ": " + i.Message))
            );
        foreach (var quest in prepared.Quests.Where(q => prepared.Missions.Any(m => m.QuestId == q.Id)))
            MissionNativeQuestChecks.RoundTrip(quest);
        draft.Definition = prepared;
        draft.Revision++;
        draft.LastEditedUtc = DateTimeOffset.UtcNow;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonConvert.SerializeObject(draft, Formatting.Indented));
        File.WriteAllText(
            output + ".manifest.json",
            JsonConvert.SerializeObject(
                new
                {
                    Source = Path.GetFullPath(source),
                    SourceHash = Convert.ToHexString(SHA256.HashData(sourceBytes)),
                    PreparedHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(output))),
                },
                Formatting.Indented
            )
        );
        Console.WriteLine($"Prepared {prepared.Missions.Single(m => m.LayoutId == layoutId).Name}; source draft preserved.");
    }

    internal static void Install(string preparedPath, string backupRoot)
    {
        var manifest = JObject.Parse(File.ReadAllText(preparedPath + ".manifest.json"));
        var destination = Path.GetFullPath((string)manifest["Source"]!);
        var expectedSource = (string)manifest["SourceHash"]!;
        var expectedPrepared = (string)manifest["PreparedHash"]!;
        string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        if (Hash(preparedPath) != expectedPrepared || Hash(destination) != expectedSource)
            throw new InvalidOperationException("The prepared or installed draft changed. Prepare it again before installing.");
        var prepared = JsonConvert.DeserializeObject<DraftEnvelope>(File.ReadAllText(preparedPath))!;
        var current = JsonConvert.DeserializeObject<DraftEnvelope>(File.ReadAllText(destination))!;
        if (
            Path.GetFileName(destination) != current.Id + ".json"
            || Path.GetFileName(Path.GetDirectoryName(destination)) != "drafts"
            || prepared.Id != current.Id
            || prepared.Definition.Id != current.Definition.Id
            || prepared.Revision != current.Revision + 1
        )
            throw new InvalidOperationException("The staged update does not match the source campaign draft.");
        var validation = SeasonValidator.Validate(prepared.Definition);
        if (!validation.CanPublish)
            throw new InvalidOperationException("The prepared mission draft no longer passes campaign validation.");
        foreach (var quest in prepared.Definition.Quests.Where(q => prepared.Definition.Missions.Any(m => m.QuestId == q.Id)))
            MissionNativeQuestChecks.RoundTrip(quest);
        var backupDirectory = Path.Combine(Path.GetFullPath(backupRoot), DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff"));
        Directory.CreateDirectory(backupDirectory);
        var backup = Path.Combine(backupDirectory, Path.GetFileName(destination));
        File.Copy(destination, backup, false);
        if (Hash(backup) != expectedSource)
            throw new IOException("The source changed while its backup was created. Nothing was installed.");
        var pending = destination + ".mission-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.Copy(preparedPath, pending, false);
            if (Hash(destination) != expectedSource || Hash(pending) != expectedPrepared)
                throw new IOException("The draft changed during installation. Nothing was installed.");
            File.Replace(pending, destination, null);
            if (Hash(destination) != expectedPrepared)
                throw new IOException("The installed draft did not match the prepared draft.");
        }
        finally
        {
            if (File.Exists(pending))
                File.Delete(pending);
        }
        Console.WriteLine($"Installed mission test draft; SHA-256 verified. Backup: {backup}");
    }

    internal static SeasonDefinition AddMission(SeasonDefinition source, string layoutId)
    {
        var season = SeasonCompiler.Copy(source);
        var layout = season.MapLayouts.Single(l => l.Id == layoutId);
        var errors = MapLayoutRules.Errors(layout, true);
        if (errors.Count != 0)
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
        if (season.Missions.Any(m => m.LayoutId == layoutId))
            return season;

        // Only the unconnected test event is changed; authored roster and transforms stay intact.
        foreach (
            var encounter in layout.Encounters.Where(e => e.Trigger.Type == MapEncounterTrigger.Event && e.Trigger.EventId == "manual")
        )
            encounter.Trigger = new() { Type = MapEncounterTrigger.MissionStart };

        season.FormatVersion = Math.Max(season.FormatVersion, 7);
        var questId = SeasonRepository.NewId();
        var conditionId = SeasonRepository.NewId();
        var variableId = SeasonRepository.NewId();
        var chapterId = SeasonRepository.NewId();
        const string traderId = "54cb50c76803fa8b248b4571";
        var title = layout.Name + ": field exercise";
        var briefing =
            $"Deploy to {layout.Location}, reach the {layout.Checkpoints.Count} route checkpoints in order, and leave through the mission exit. Hostile Scavs may be avoided. The mission remains available for replay.";
        var quest = new NativeQuest
        {
            Id = questId,
            QuestName = title,
            Name = questId + " name",
            Description = questId + " description",
            TraderId = traderId,
            Location = "any",
            Image = "/files/quest/icon/596b36c586f77450d6045ad2.jpg",
            Type = "Exploration",
            Side = "Pmc",
            CanShowNotificationsInGame = true,
            Restartable = false,
            Conditions = new()
            {
                AvailableForStart =
                [
                    new()
                    {
                        Id = SeasonRepository.NewId(),
                        ConditionType = "Level",
                        Value = 1,
                        CompareMethod = ">=",
                        DynamicLocale = false,
                    },
                ],
                AvailableForFinish =
                [
                    new()
                    {
                        Id = conditionId,
                        ConditionType = "GlobalVariableValue",
                        Target = variableId,
                        Value = 1,
                        CompareMethod = ">=",
                        DynamicLocale = false,
                    },
                ],
            },
            Rewards = new()
            {
                ["Started"] = [],
                ["Success"] = [],
                ["Fail"] = [],
            },
            Localization = new()
            {
                ["en"] = new()
                {
                    [questId + " name"] = title,
                    [questId + " description"] = briefing,
                    [conditionId] = $"Complete {layout.Name} and extract through its mission exit",
                },
            },
        };
        quest.StartedMessageText = questId + " startedMessageText";
        quest.SuccessMessageText = questId + " successMessageText";
        quest.FailMessageText = questId + " failMessageText";
        quest.AcceptPlayerMessage = questId + " acceptPlayerMessage";
        quest.CompletePlayerMessage = questId + " completePlayerMessage";
        quest.DeclinePlayerMessage = questId + " declinePlayerMessage";
        foreach (
            var key in new[]
            {
                quest.StartedMessageText,
                quest.SuccessMessageText,
                quest.FailMessageText,
                quest.AcceptPlayerMessage,
                quest.CompletePlayerMessage,
                quest.DeclinePlayerMessage,
            }
        )
            quest.Localization["en"][key] = briefing;
        season.Quests.Add(quest);
        season.Story ??= new();
        season.Story.Chapters.Add(
            new()
            {
                Id = chapterId,
                Name = "Mission training",
                Order = season.Story.Chapters.Count,
            }
        );
        season.Story.Variables.Add(
            new()
            {
                Id = variableId,
                Scope = StoryVariableScope.Profile,
                InitialValue = 0,
            }
        );
        season.Story.Quests.Add(
            new()
            {
                QuestId = questId,
                ChapterId = chapterId,
                Main = true,
                AutoStart = false,
                AutoComplete = false,
            }
        );
        season.Missions.Add(
            new MissionDefinition
            {
                Id = SeasonRepository.NewId(),
                Name = layout.Name,
                Briefing = briefing,
                LayoutId = layoutId,
                QuestId = questId,
                CompletionConditionId = conditionId,
            }
        );
        return season;
    }

    internal static void Run(Action<bool, string> check)
    {
        string Id() => SeasonRepository.NewId();
        var layout = new MapLayout
        {
            Id = Id(),
            Name = "Test",
            Location = "Interchange",
            Start = new()
            {
                Id = Id(),
                Location = "Interchange",
                Scene = "Test",
            },
            Checkpoints =
            [
                new()
                {
                    Id = Id(),
                    Location = "Interchange",
                    Scene = "Test",
                },
                new()
                {
                    Id = Id(),
                    Location = "Interchange",
                    Scene = "Test",
                },
            ],
            Exit = new()
            {
                Id = Id(),
                Location = "Interchange",
                Scene = "Test",
            },
        };
        var original = new SeasonDefinition
        {
            Id = Id(),
            BattlePassId = Id(),
            Name = "New campaign",
            MapLayouts = [layout],
        };
        var before = JObject.FromObject(original);
        var prepared = AddMission(original, layout.Id);
        check(JToken.DeepEquals(before, JObject.FromObject(original)), "Mission setup never mutates its source draft");
        check(
            prepared.Id == original.Id && prepared.MapLayouts[0].Id == layout.Id,
            "Mission setup preserves campaign and layout identities"
        );
        check(
            JToken.DeepEquals(JToken.FromObject(layout), JToken.FromObject(prepared.MapLayouts[0])),
            "Mission setup preserves route geometry"
        );
        var mission = prepared.Missions.Single();
        var quest = prepared.Quests.Single(q => q.Id == mission.QuestId);
        check(
            quest.TraderId == "54cb50c76803fa8b248b4571" && quest.Rewards.Values.All(r => r.Count == 0),
            "Test quest uses Prapor without extra rewards"
        );
        check(quest.Conditions.AvailableForFinish.Single().Id == mission.CompletionConditionId, "Test mission links its native objective");
        check(
            !prepared.Story!.Quests.Single().AutoStart && !prepared.Story.Quests.Single().AutoComplete,
            "Test quest requires acceptance and turn-in"
        );
        check(
            JToken.DeepEquals(JToken.FromObject(prepared), JToken.FromObject(AddMission(prepared, layout.Id))),
            "Mission setup is idempotent"
        );
        var encounterSource = SeasonCompiler.Copy(original);
        encounterSource
            .MapLayouts[0]
            .Encounters.Add(
                new()
                {
                    Id = Id(),
                    Trigger = new() { Type = MapEncounterTrigger.Event, EventId = "manual" },
                    Waves =
                    [
                        new()
                        {
                            Id = Id(),
                            Roster =
                            [
                                new()
                                {
                                    Id = Id(),
                                    Role = "assault",
                                    Difficulty = "hard",
                                    Count = 3,
                                },
                            ],
                        },
                    ],
                }
            );
        var encounterBefore = JToken.FromObject(encounterSource);
        var encounterPrepared = AddMission(encounterSource, layout.Id);
        var authored = encounterPrepared.MapLayouts[0].Encounters.Single();
        check(
            authored.Trigger.Type == MapEncounterTrigger.MissionStart && authored.Trigger.EventId.Length == 0,
            "Test setup replaces the unconnected manual event with mission start"
        );
        check(
            JToken.DeepEquals(JToken.FromObject(authored.Waves), JToken.FromObject(encounterSource.MapLayouts[0].Encounters[0].Waves)),
            "Test setup preserves all three hard Scavs and their authored wave identities"
        );
        check(JToken.DeepEquals(encounterBefore, JToken.FromObject(encounterSource)), "Encounter setup preserves its input draft");
        authored.Trigger = SeasonCompiler.Copy(encounterSource.MapLayouts[0].Encounters[0].Trigger);
        check(
            JToken.DeepEquals(JToken.FromObject(encounterPrepared.MapLayouts), JToken.FromObject(encounterSource.MapLayouts)),
            "The trigger is the only changed field in the existing authored layout"
        );
    }
}
