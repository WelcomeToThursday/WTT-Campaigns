using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SeasonalPerks.Shared.Contracts;
using SeasonalPerks.Shared.Hub;

namespace SeasonalPerks.Tests;

internal static class HubGameplayChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "data");
        var presentation = JsonConvert.DeserializeObject<HubState>(File.ReadAllText(Path.Combine(path, "hub.json")))!;
        var catalogue = JObject.Parse(File.ReadAllText(Path.Combine(path, "hub-gameplay.json")));
        var rewards = presentation.Pages.SelectMany(p => p.Rewards).ToArray();
        check(rewards.Length == 53 && presentation.Pages.Length == 12, "Complete Battle Pass tile catalogue");
        check(rewards.Sum(r => r.Costs.Sum(c => c.Count)) == 501, "Amended document costs total 501");
        check(rewards.Sum(r => catalogue["Rewards"]![r.Id]!["Grants"]!.Count()) == 58, "All 58 Battle Pass payloads retained");
        check(presentation.Documents.Length == 8 && presentation.SeasonalRewards.Length == 5, "Document and seasonal reward catalogues");
        foreach (var reward in rewards.Concat(presentation.SeasonalRewards))
        {
            var definition = catalogue["Rewards"]![reward.Id]!;
            check(
                definition["Grants"]!.All(g => g["type"]!.ToString() is "Item" or "CustomizationDirect" or "AssortmentUnlock" or "Tarcoin"),
                "Explicit adapter for " + reward.Id
            );
            check(
                definition["Conditions"]!.All(c => c["conditionType"]!.ToString() is "Level" or "Quest"),
                "Structured conditions for " + reward.Id
            );
        }

        var state = new HubProgress();
        var raid = new HubRaid();
        const long start = 100000;
        check(HubRules.Remaining(state, start) == 30 && state.WindowStart == 0, "Reading allowance does not start its window");
        HubProvenance.Register(raid, "brought", "financial", 17, false);
        HubProvenance.Register(raid, "loot", "financial", 31, true);
        foreach (var unit in raid.Stacks["loot"].Units)
        {
            HubRules.Pickup(state, raid, unit, start);
        }
        check(state.Pickups == 30 && raid.Rejected.Count == 1 && state.WindowStart == start, "First 30 units qualify; excess is rejected");
        HubProvenance.Transfer(raid, "loot", "brought", 10, false);
        HubProvenance.Transfer(raid, "brought", "split", 20, true);
        check(raid.Stacks["split"].Units.Count(u => raid.Spawned.ContainsKey(u)) == 3, "Split preserves 17 brought-in and three new units");
        foreach (var unit in raid.Stacks.Values.SelectMany(s => s.Units))
        {
            HubRules.Pickup(state, raid, unit, start + 10);
        }
        check(state.Pickups == 30, "Merging, splitting and reacquiring cannot count another pickup");
        var persisted = JsonConvert.DeserializeObject<HubRaid>(JsonConvert.SerializeObject(raid))!;
        check(
            persisted.Stacks.Values.SelectMany(s => s.Units).Distinct().Count() == 48,
            "Restart preserves every unit identity exactly once"
        );
        check(HubRules.Remaining(state, start + HubRules.WindowSeconds - 1) == 0, "Allowance remains closed until exactly 23 hours");
        check(HubRules.Remaining(state, start + HubRules.WindowSeconds) == 30, "Allowance resets after 23 hours");
        check(
            !HubRules.Pickup(state, raid, "loot:30", start + HubRules.WindowSeconds),
            "Previously rejected pickup cannot be relabelled after reset"
        );
        HubProvenance.Register(raid, "later", "medical", 1, true);
        check(HubRules.Pickup(state, raid, "later:0", start + HubRules.WindowSeconds), "New first pickup opens the next window");
        check(state.Pickups == 1 && state.WindowStart == start + HubRules.WindowSeconds, "Only new window pickups count");
        var rolls = 0;
        var bonus = HubRules.Finish(
            state,
            raid,
            persisted.Stacks["split"].Units.Concat(persisted.Stacks["split"].Units),
            true,
            () =>
            {
                rolls++;
                return 4;
            }
        );
        check(
            bonus == 3 && rolls == 3 && state.Classified == 3,
            "Only newly extracted units roll, once each, at the five-percent boundary"
        );
        check(
            HubRules.Finish(state, raid, raid.Spawned.Keys, true, () => 0) == 3 && state.Classified == 3,
            "Repeated extraction returns its receipt without regranting"
        );
        var failed = new HubRaid();
        HubProvenance.Register(failed, "failed", "medical", 1, true);
        HubRules.Pickup(state, failed, "failed:0", start + HubRules.WindowSeconds);
        check(
            HubRules.Finish(state, failed, failed.Spawned.Keys, false, () => throw new Exception("Failed raid rolled")) == 0,
            "Failed raid never rolls a Classified bonus"
        );
        var boundary = new HubRaid();
        HubProvenance.Register(boundary, "boundary", "medical", 1, true);
        HubRules.Pickup(state, boundary, "boundary:0", start + HubRules.WindowSeconds);
        check(
            HubRules.Finish(state, boundary, boundary.Spawned.Keys, true, () => 5) == 0,
            "Roll five is outside the five-percent success interval"
        );
        check(
            HubRules.Shortage(new Dictionary<string, int> { ["a"] = 5, ["b"] = 3 }, new Dictionary<string, int> { ["a"] = 10, ["b"] = 1 })
                == 2,
            "Surplus of one document does not cover another type"
        );
        var invalid = new HubRaid();
        HubProvenance.Register(invalid, "one", "a", 1, true);
        HubProvenance.Register(invalid, "two", "b", 1, true);
        Throws(() => HubProvenance.Transfer(invalid, "one", "two", 1, false), "Cross-template transfer rejected");
        Throws(() => HubProvenance.Transfer(invalid, "one", "new", 2, true), "Split cannot mint units");
        Throws(() => HubProvenance.Transfer(invalid, "one", "one", 1, false), "Self-transfer rejected");
        void Throws(Action action, string name)
        {
            try
            {
                action();
            }
            catch (InvalidOperationException)
            {
                check(true, name);
                return;
            }
            check(false, name);
        }
    }
}
