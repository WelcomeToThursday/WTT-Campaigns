using Mono.Cecil;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Utils.Json;
using WTT.Campaigns.Server.Progression;
using WTT.Campaigns.Shared.Progression;
using Path = System.IO.Path;

namespace WTT.Campaigns.Tests;

internal static class ProgressionChecks
{
    internal static void Database(string database)
    {
        var native = JObject.Parse(File.ReadAllText(Path.Combine(database, "templates", "quests.json")));
        var overlay = JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "trader-progression.json")));
        var options = new System.Text.Json.JsonSerializerOptions();
        foreach (var converter in new SptJsonConverterRegistrator().GetJsonConverters())
        {
            options.Converters.Add(converter);
        }
        var count = 0;
        foreach (var entry in ((JObject)overlay["Quests"]!).Properties())
        {
            var spec = entry.Value.ToObject<TaskProgression>()!;
            var original = (JArray)native[entry.Name]!["conditions"]!["AvailableForStart"]!;
            var start = (JArray)entry.Value["Start"]!;
            if (
                spec.Tier > 0
                    ? spec.UseBetaStart || !JToken.DeepEquals(new JArray(start.Take(original.Count)), original)
                    : !spec.UseBetaStart || start.Count != 0
            )
            {
                throw new Exception(entry.Name + " must retain every native start requirement");
            }

            var quest = System.Text.Json.JsonSerializer.Deserialize<Quest>(native[entry.Name]!.ToString(), options)!;
            var before = System.Text.Json.JsonSerializer.Serialize(quest.Conditions.AvailableForStart, options);
            // Deliberately mimic the old DLL-only installation, with no packaged
            // native conditions and a false fallback flag even for Essentials.
            spec.Start.Clear();
            spec.UseBetaStart = false;
            QuestStartRequirements.Apply(quest, spec);
            var actual = quest.Conditions.AvailableForStart ?? throw new Exception(entry.Name + " has no runtime start conditions");
            if (
                before != System.Text.Json.JsonSerializer.Serialize(actual.Take(original.Count).ToList(), options)
                || spec.Tier == 0 && actual.Count != original.Count
            )
            {
                throw new Exception(entry.Name + " runtime hotfix lost native requirements with legacy data");
            }
            if (entry.Name == "59c512ad86f7741f0d09de9b")
            {
                Punisher(quest);
            }
            count++;
        }
        Console.WriteLine(
            $"Verified packaged and DLL-only native requirements for all {count} tasks, including Essentials and Punisher Part 3."
        );
    }

    private static void Punisher(Quest quest)
    {
        var profile = new PmcData
        {
            Info = new() { Level = 1 },
            Quests = [],
        };
        void Expect(bool available, string message)
        {
            if (QuestStartRequirements.CanStart(quest, profile, 10000) != available)
            {
                throw new Exception("Punisher Part 3: " + message);
            }
        }
        Expect(false, "fresh character must not be eligible");
        profile.Info.Level = 19;
        Expect(false, "level alone cannot bypass Part 2");
        profile.Quests.Add(
            new()
            {
                QId = "59c50c8886f7745fed3193bf",
                Status = QuestStatusEnum.Started,
                StartTime = 0,
                StatusTimers = [],
            }
        );
        Expect(false, "accepting Part 2 does not complete it");
        profile.Quests[0].Status = QuestStatusEnum.Fail;
        Expect(false, "failing Part 2 does not complete it");
        profile.Quests[0].Status = QuestStatusEnum.Success;
        profile.Info.Level = 18;
        Expect(false, "Part 2 completion cannot bypass level 19");
        profile.Info.Level = 19;
        Expect(true, "level 19 and completed Part 2 must unlock the quest");
    }

    internal static void Run(Action<bool, string> check)
    {
        var data = JsonConvert.DeserializeObject<TraderProgression>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "trader-progression.json"))
        )!;
        check(data.Version == 1 && data.Quests.Count == 381, "381 existing tasks in progression overlay");
        check(data.Quests.Values.Count(q => q.Tier > 0) == 238, "238 loyalty-group tasks");
        foreach (
            var (id, prerequisite, level) in new[]
            {
                ("5936d90786f7742b1420ba5b", "657315df034d76585f032e01", 1), // Debut after Shooting Cans
                ("5936da9e86f7742d65037edf", "657315e1dccd301f1301416a", 2), // Background Check after Luxurious Life
                ("59674cd986f7744ab26e32f2", "5936da9e86f7742d65037edf", 3), // Shootout Picnic after Background Check
                ("5fd9fad9c1ce6b1a3b486d00", "5936d90786f7742b1420ba5b", 5), // Search Mission after Debut
            }
        )
        {
            var start = data.Quests[id].Start;
            check(
                start.Any(c => c.ConditionType == "Quest" && (string?)c.Target == prerequisite && c.Status!.Contains("4")),
                id + " retains its quest chain instead of unlocking at LL1"
            );
            check(
                start.Any(c => c.ConditionType == "Level" && c.Value == level && c.CompareMethod == ">="),
                id + " retains its player-level requirement"
            );
        }
        check(
            data.Quests["657315df034d76585f032e01"].Start.All(c => c.ConditionType == "TraderLoyalty"),
            "Shooting Cans remains a genuine LL1 starter task"
        );
        foreach (var (id, levels) in data.Traders)
        {
            for (var i = 1; i < levels.Count; i++)
            {
                var threshold = levels[i];
                check(
                    TraderProgression.Loyalty(levels, threshold.Level, threshold.Standing) == i + 1,
                    id + " exact LL" + (i + 1) + " threshold"
                );
                check(
                    TraderProgression.Loyalty(levels, threshold.Level, threshold.Standing - .00001) < i + 1,
                    id + " reputation just below threshold"
                );
                check(
                    TraderProgression.Loyalty(levels, threshold.Level - 1, threshold.Standing) < i + 1,
                    id + " player level just below threshold"
                );
            }
            check(TraderProgression.Loyalty(levels, 79, 100) == levels.Count, id + " maximum loyalty including Fence");
            check(TraderProgression.Loyalty(levels, 1, -10) == 1, id + " negative standing retains base loyalty");
        }
        check(!TraderProgression.Compare(.69, .7, ">="), "Fractional standing must not round down required value");
        check(TraderProgression.Compare(.7, .7, ">="), "Fractional standing boundary includes equality");
        check(!TraderProgression.Compare(.7, .7, ">"), "Strict standing comparison excludes equality");
        foreach (var (id, quest) in data.Quests)
        {
            check(quest.Tier is >= 0 and <= 4, id + " valid display tier");
            check(quest.UseBetaStart == (quest.Tier == 0), id + " every essential task retains native start rules");
            check(
                quest.Start.All(c => (string?)c.ConditionType is "Level" or "Quest" or "TraderLoyalty" or "TraderStanding"),
                id + " no unsupported global variables reach beta"
            );
            if (quest.Tier > 0)
            {
                check(
                    quest.Start.Any(c =>
                        (string?)c.ConditionType == "TraderLoyalty"
                        && (string?)c.Target == quest.TraderId
                        && c.CompareMethod == ">="
                        && (int?)c.Value >= quest.Tier
                    ),
                    id + " explicit loyalty gate"
                );
            }
            check(
                quest.Reputation.Values.SelectMany(v => v).All(r => (string?)r.Type == "TraderStanding"),
                id + " only reputation rewards in overlay"
            );
        }
    }

    internal static void Hooks(string game, Action<bool, string> check)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(game);
        var types = assembly.MainModule.GetTypes().ToDictionary(t => t.FullName);
        var list = types["EFT.UI.QuestsListView"];
        foreach (var name in new[] { "Show", "UpdateVisibility", "QuestAddedHandler", "AutoSelectQuest" })
        {
            check(list.Methods.Count(m => m.Name == name) == 1, "Unique native task list hook " + name);
        }
        foreach (var field in new[] { "_questListContainer", "_questListItemPrefab", "_toggleShowCompleted", "_toggleShowLocked" })
        {
            check(list.Fields.Any(f => f.Name == field), "Native task list field " + field);
        }
        check(types["EFT.UI.QuestListItem"].Fields.Any(f => f.Name == "_title"), "Native row font source");
        check(types["EFT.UI.QuestView"].Fields.Any(f => f.Name == "_title"), "Native quest tier badge anchor");
        check(
            types["EFT.UI.TradingPlayerPanel"].Fields.Any(f => f.Name == "_currentRank" && f.FieldType.FullName == "EFT.UI.RankPanel"),
            "Native trader rank badge source"
        );
        check(
            types["EFT.UI.RankPanel"].Methods.Any(m => m.Name == "Show" && m.Parameters.Count == 2),
            "Native rank badge supports task Roman numerals"
        );
        check(
            types["EFT.UI.TraderTooltip"].Methods.Any(m => m.Name == "Show" && m.Parameters.Single().Name == "traderInfo"),
            "Trader tooltip patch argument binding"
        );
        var update = types["EFT.UI.TradingPlayerPanel"].Methods.Single(m => m.Name == "UpdateStats");
        check(
            update.Body.Instructions.Any(i => i.Operand is FieldReference f && f.Name == "MinSalesSum")
                && update.Body.Instructions.Any(i =>
                    i.Operand is FieldReference f && f.Name == "Empty" && f.DeclaringType.FullName == "System.String"
                ),
            "Native header supports empty spending requirement"
        );
    }
}
