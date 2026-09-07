using Mono.Cecil;
using Newtonsoft.Json;
using SeasonalPerks.Shared.Progression;

namespace SeasonalPerks.Tests;

internal static class ProgressionChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var data = JsonConvert.DeserializeObject<TraderProgression>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "trader-progression.json"))
        )!;
        check(data.Version == 1 && data.Quests.Count == 381, "381 existing tasks in progression overlay");
        check(data.Quests.Values.Count(q => q.Tier > 0) == 238, "238 loyalty-group tasks");
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
            check(!quest.UseBetaStart || quest.Tier == 0, id + " only essential tasks fall back to beta start rules");
            check(
                quest.Start.All(c => (string?)c["conditionType"] is "Level" or "Quest" or "TraderLoyalty" or "TraderStanding"),
                id + " no unsupported global variables reach beta"
            );
            if (quest.Tier > 0)
            {
                check(
                    quest.Start.Any(c =>
                        (string?)c["conditionType"] == "TraderLoyalty"
                        && (string?)c["target"] == quest.TraderId
                        && (int?)c["value"] == quest.Tier
                    ),
                    id + " explicit loyalty gate"
                );
            }
            check(
                quest.Reputation.Values.SelectMany(v => v).All(r => (string?)r["type"] == "TraderStanding"),
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
