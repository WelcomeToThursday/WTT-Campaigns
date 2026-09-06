using Newtonsoft.Json.Linq;
using SeasonalPerks.Shared.Effects;

namespace SeasonalPerks.Shared.Seasons;

public static class SeasonValidator
{
    public static bool IsId(string? id)
    {
        return id != null && id.Length == 24 && id.All(c => "0123456789abcdef".Contains(c));
    }

    public static SeasonValidationResult Validate(SeasonDefinition season)
    {
        var result = new SeasonValidationResult();
        try
        {
            ValidateCore(season, result);
        }
        catch (Exception e)
            when (e is NullReferenceException or InvalidCastException or ArgumentException or FormatException or OverflowException)
        {
            result.Add("Overview", "The definition contains a null collection or malformed field: " + e.Message);
        }
        return result;
    }

    private static void ValidateCore(SeasonDefinition s, SeasonValidationResult r)
    {
        void Need(bool ok, string path, string message)
        {
            if (!ok)
            {
                r.Add(path, message);
            }
        }
        Need(s.FormatVersion == 1, "Overview", "Unsupported season format version.");
        Need(IsId(s.Id) && IsId(s.BattlePassId), "Overview", "Season and battle pass require valid identities.");
        Need(!string.IsNullOrWhiteSpace(s.Name) && s.Name.Length <= 120, "Overview", "Name is required (up to 120 characters).");
        Need(s.Rules.StartingPoints is >= 0 and <= 100000, "Perks", "Starting budget must be 0–100000.");
        Need(IsId(s.UniversalImage) && IsId(s.UniversalUnavailableImage), "Assets", "Choose both Classified document images.");
        Need(s.Locales.ContainsKey("en"), "Localization", "English fallback is required.");
        Need(s.Documents.Count is >= 1 and <= 8, "Documents", "Choose between one and eight document types.");
        Need(
            s.Documents.Select(d => d.ItemId).Distinct().Count() == s.Documents.Count,
            "Documents",
            "Each document requires a different item template."
        );
        Need(
            s.Collection.DocumentsPerRaid is >= 0 and <= 8 && s.Collection.MapCounts.Values.All(n => n is >= 0 and <= 8),
            "Documents",
            "Raid and map caps must be between zero and eight."
        );
        Need(
            s.Collection.DocumentLimit is >= 1 and <= 100000 && s.Collection.WindowSeconds is >= 60 and <= 31536000,
            "Documents",
            "Allowance must be 1–100000 and window 60–31536000 seconds."
        );
        Need(s.Collection.ClassifiedChancePercent is >= 0 and <= 100, "Documents", "Classified chance must be 0–100 percent.");
        Need(s.ExchangeRate is >= 1 and <= 100000 && s.CrateCost is >= 1 and <= 100000, "Exchanges", "Exchange costs must be 1–100000.");
        if (s.Items.Count > 1000 || s.Quests.Count > 1000 || s.Perks.All.Count() > 2000 || s.AllRewards.Count() > 610)
        {
            r.Add("Overview", "Pack content exceeds authoring limits (1000 items/quests, 2000 perks, 610 reward tiles).");
            return;
        }
        var ids = new HashSet<string>();
        void Identity(string id, string path)
        {
            Need(IsId(id), path, "Invalid identity: " + id);
            Need(ids.Add(id), path, "Duplicate owned identity: " + id);
        }
        Identity(s.Id, "Overview");
        Identity(s.BattlePassId, "Overview");
        foreach (var d in s.Documents)
        {
            Identity(d.Id, "Documents");
            Need(IsId(d.ItemId), "Documents", "Choose an item template for " + d.Name);
            Need(IsId(d.Image) && IsId(d.UnavailableImage), "Assets", "Choose both images for " + d.Name);
        }
        foreach (var item in s.Items)
        {
            Identity(item.Id, "Items");
            Need(IsId(item.CloneFrom), "Items", "Choose a compatible source item.");
            Need(
                item.Width is >= 1 and <= 10 && item.Height is >= 1 and <= 10 && item.StackMax is >= 1 and <= 100000,
                "Items",
                "Invalid dimensions or stack limit for " + item.Name
            );
        }
        foreach (var item in s.ImportedItems.Properties())
        {
            Identity(item.Name, "Items");
        }

        foreach (var item in s.Items)
        {
            var seen = new HashSet<string>();
            var next = item;
            while (next != null)
            {
                if (!seen.Add(next.Id))
                {
                    r.Add("Items/" + item.Id, "Item clone dependency cycle.");
                    break;
                }
                next = s.Items.FirstOrDefault(i => i.Id == next.CloneFrom);
            }
        }
        foreach (var perk in s.Perks.All)
        {
            var path = "Perks/" + perk.Id;
            Identity(perk.Id, path);
            Need(IsId(perk.ImageUrl), path, "Choose a perk icon.");
            var unavailable = EffectSupport.UnavailableReason(perk);
            if (unavailable != null)
            {
                r.Add(path, unavailable, s.Legacy || !perk.Enabled ? "warning" : "error");
            }

            Need(
                perk.Conflicts.All(id => id != perk.Id && s.Perks.Personal.Any(p => p.Id == id)),
                path,
                "Conflicts must reference other personal perks."
            );
            Need(perk.Points is null or (>= -100000 and <= 100000), path, "Point value is outside the supported range.");
            foreach (var effect in perk.Effects)
            {
                var error = EffectParametersValidator.Error(effect);
                if (error != null && perk.Enabled)
                {
                    r.Add(path, error, s.Legacy ? "warning" : "error");
                }
            }
            Need(
                s.Locales["en"].TryGetValue(perk.Id + " name", out var name) && !string.IsNullOrWhiteSpace(name),
                path,
                "Add an English perk name."
            );
        }
        Need(
            s.Rules.EnabledCommonIds.All(id => s.Perks.Common.Any(p => p.Id == id && EffectSupport.UnavailableReason(p) == null)),
            "Perks",
            "Common rules include an unknown or unsupported perk."
        );
        Need(s.Pages.Count is >= 1 and <= 100, "Battle pass", "Use 1–100 battle pass pages.");
        for (var page = 0; page < s.Pages.Count; page++)
        {
            var p = s.Pages[page];
            Need(
                p.PreviousRequirement >= 0 && p.PreviousRequirement <= (page == 0 ? 0 : s.Pages[page - 1].Rewards.Count(x => x.Enabled)),
                "Battle pass/" + page,
                "The previous-page requirement is impossible."
            );
            Grid(p.Rewards, 2, 3, "Battle pass/" + page);
        }
        Grid(s.SeasonalRewards, 5, 2, "Rewards");
        foreach (var reward in s.AllRewards)
        {
            var path = "Rewards/" + reward.Id;
            Identity(reward.Id, path);
            Need(reward.Side is "" or "USEC" or "BEAR" or "Usec" or "Bear", path, "Unknown faction.");
            Need(
                reward.Costs.Select(c => c.DocumentId).Distinct().Count() == reward.Costs.Count,
                path,
                "Combine duplicate document costs."
            );
            Need(
                reward.Costs.All(c => c.Count is >= 1 and <= 100000 && s.Documents.Any(d => d.Id == c.DocumentId)),
                path,
                "Costs require known documents and positive quantities."
            );
            if (!reward.Enabled)
            {
                continue;
            }

            Need(IsId(reward.Image) && IsId(reward.BigImage), path, "Choose thumbnail and full artwork.");
            Need(reward.Grants.All(g => g is JObject) && reward.Conditions.All(c => c is JObject), path, "Malformed payload or condition.");
            Need(reward.Grants.Count > 0, path, "Add at least one reward payload.");
            foreach (var grant in reward.Grants.OfType<JObject>())
            {
                var kind = (string?)grant["type"];
                Need(kind is "Item" or "Tarcoin" or "CustomizationDirect" or "AssortmentUnlock", path, "Unsupported reward type: " + kind);
                if (kind == "Tarcoin")
                {
                    Need((long?)grant["value"] is >= 1 and <= 1000000000, path, "Tarcoin amount must be positive and at most one billion.");
                }

                if (kind is "CustomizationDirect" or "AssortmentUnlock")
                {
                    Need(IsId((string?)grant["target"]), path, "Select a reward target.");
                }

                if (kind is "Item" or "AssortmentUnlock")
                {
                    ItemTree(grant["items"] as JArray, path, r);
                }

                if (kind == "AssortmentUnlock")
                {
                    Need(
                        IsId((string?)grant["traderId"]) && (int?)grant["loyaltyLevel"] is >= 1 and <= 4,
                        path,
                        "Select a trader and loyalty level 1–4."
                    );
                }
            }
            foreach (var condition in reward.Conditions.OfType<JObject>())
            {
                var kind = (string?)condition["conditionType"];
                Need(kind is "Level" or "Quest", path, "Reward requirements support level and completed quests.");
                if (kind == "Level")
                {
                    Need((int?)condition["value"] is >= 1 and <= 100, path, "Level requirement must be 1–100.");
                }

                if (kind == "Quest")
                {
                    Need(IsId((string?)condition["target"]), path, "Select a required quest.");
                }

                if (kind == "Level")
                {
                    Need(
                        condition["compareMethod"] == null || (string?)condition["compareMethod"] == ">=",
                        path,
                        "Level requirements use minimum levels."
                    );
                }
            }
        }
        Need(
            s.Crates.Select(c => c.ItemId).Distinct().Count() == s.Crates.Count,
            "Exchanges",
            "Crate definitions must have unique item templates."
        );
        foreach (var crate in s.Crates)
        {
            Need(
                IsId(crate.ItemId)
                    && crate.RewardCount is >= 1 and <= 100
                    && crate.Pool.Count > 0
                    && crate.Pool.All(p => IsId(p.Key) && p.Value > 0 && !double.IsInfinity(p.Value)),
                "Exchanges",
                "Crates require a template, 1–100 rewards, and positive finite item weights."
            );
        }

        var quests = s.Quests.OfType<JObject>().ToDictionary(q => (string)q["_id"]!);
        var visitedQuests = new HashSet<string>();
        foreach (var quest in quests)
        {
            Identity(quest.Key, "Quests/" + quest.Key);
            if ((bool?)quest.Value["_seasonalEnabled"] == false)
            {
                continue;
            }

            foreach (var condition in quest.Value.Descendants().OfType<JObject>().Where(c => c["conditionType"] != null))
            {
                var kind = (string?)condition["conditionType"];
                if (kind is not ("Quest" or "Level" or "TraderLoyalty" or "FindItem" or "HandoverItem"))
                {
                    r.Add("Quests/" + quest.Key, "Unsupported objective: " + kind, s.Legacy ? "warning" : "error");
                }
            }
            if (!s.Legacy)
            {
                var path = "Quests/" + quest.Key;
                Need(
                    quest.Value["conditions"] is JObject
                        && quest.Value["rewards"] is JObject
                        && quest.Value["localization"]?["en"] is JObject,
                    path,
                    "Quest requires conditions, rewards, and English text."
                );
                foreach (var stage in new[] { "AvailableForStart", "AvailableForFinish", "Fail" })
                {
                    Need(quest.Value["conditions"]?[stage] is JArray, path, "Missing quest stage: " + stage);
                }

                foreach (
                    var condition in ((JContainer)quest.Value["conditions"]!)
                        .Descendants()
                        .OfType<JObject>()
                        .Where(c => c["conditionType"] != null)
                )
                {
                    var kind = (string?)condition["conditionType"];
                    Need(IsId((string?)condition["id"]), path, "Objective requires an identity.");
                    Need((double?)condition["value"] is >= 1 and <= 1000000, path, "Objective requires a positive quantity or level.");
                    if (kind is "FindItem" or "HandoverItem")
                    {
                        Need(
                            condition["target"] is JArray targets && targets.Count > 0 && targets.All(t => IsId((string?)t)),
                            path,
                            "Select objective items."
                        );
                    }

                    if (kind is "Quest" or "TraderLoyalty")
                    {
                        Need(IsId((string?)condition["target"]), path, "Select the prerequisite quest or trader.");
                    }

                    if (kind == "Level")
                    {
                        Need((int?)condition["value"] is >= 1 and <= 100, path, "Quest level must be 1–100.");
                    }

                    if (kind == "TraderLoyalty")
                    {
                        Need((int?)condition["value"] is >= 1 and <= 4, path, "Trader loyalty must be 1–4.");
                    }

                    if (kind == "Quest")
                    {
                        Need(
                            condition["status"] is JArray statuses && statuses.Count == 1 && (int?)statuses[0] == 4,
                            path,
                            "Quest prerequisites require completed quests."
                        );
                    }
                }
                foreach (var grant in quest.Value["rewards"]!.Children<JProperty>().SelectMany(p => p.Value).OfType<JObject>())
                {
                    var kind = (string?)grant["type"];
                    Need(
                        kind
                            is "Item"
                                or "Experience"
                                or "TraderStanding"
                                or "TraderUnlock"
                                or "AssortmentUnlock"
                                or "TraderStandingRestore"
                                or "Skill"
                                or "Customization",
                        path,
                        "Unsupported native quest reward: " + kind
                    );
                    if (kind is "Item" or "AssortmentUnlock")
                    {
                        ItemTree(grant["items"] as JArray, path, r);
                    }
                }
            }
            Visit(quest.Key, new HashSet<string>());
        }
        foreach (var faction in new[] { s.Starting.Usec, s.Starting.Bear })
        {
            Need(faction.Skills.Values.All(n => n is >= 0 and <= 51), "Starting character", "Starting skill levels must be 0–51.");
            Need(
                faction.Items.All(i => IsId(i.Template) && i.Count is >= 1 and <= 10000000 && (i.Slot.Length == 0 || i.Count == 1)),
                "Starting character",
                "Choose valid starter items and quantities; equipment slots hold one item."
            );
            Need(
                faction.Items.Where(i => i.Slot.Length > 0).Select(i => i.Slot).Distinct().Count()
                    == faction.Items.Count(i => i.Slot.Length > 0),
                "Starting character",
                "Only one replacement per equipment slot."
            );
        }
        void Visit(string id, HashSet<string> visiting)
        {
            if (visitedQuests.Contains(id) || !quests.TryGetValue(id, out var quest))
            {
                return;
            }

            if (visiting.Count > 128)
            {
                r.Add("Quests/" + id, "Quest chain exceeds 128 levels.");
                return;
            }
            if (!visiting.Add(id))
            {
                r.Add("Quests/" + id, "Quest dependency cycle.", s.Legacy ? "warning" : "error");
                return;
            }
            foreach (var c in quest.Descendants().OfType<JObject>().Where(c => (string?)c["conditionType"] == "Quest"))
            {
                foreach (var target in c["target"] is JArray a ? a.Values<string>() : new[] { (string?)c["target"] })
                {
                    if (target != null)
                    {
                        Visit(target, new HashSet<string>(visiting));
                    }
                }
            }

            visitedQuests.Add(id);
        }
        void Grid(IEnumerable<SeasonReward> rewards, int columns, int rows, string path)
        {
            var cells = new HashSet<(int, int)>();
            foreach (var tile in rewards.Where(t => t.Enabled))
            {
                Need(
                    tile.X >= 0
                        && tile.Y >= 0
                        && tile.Width >= 1
                        && tile.Height >= 1
                        && tile.X + (long)tile.Width <= columns
                        && tile.Y + (long)tile.Height <= rows,
                    path,
                    "Tile outside the grid: " + tile.Name
                );
                if (tile.Width > columns || tile.Height > rows)
                {
                    continue;
                }

                for (var x = tile.X; x < tile.X + tile.Width; x++)
                {
                    for (var y = tile.Y; y < tile.Y + tile.Height; y++)
                    {
                        Need(cells.Add((x, y)), path, "Overlapping tile: " + tile.Name);
                    }
                }
            }
        }
    }

    public static void ItemTree(JArray? items, string path, SeasonValidationResult result)
    {
        if (items == null || items.Count == 0)
        {
            result.Add(path, "An item payload needs an item tree.");
            return;
        }
        var nodes = items.OfType<JObject>().ToList();
        var ids = nodes.Select(i => (string?)i["_id"]).ToList();
        if (
            nodes.Count != items.Count
            || ids.Any(id => !IsId(id))
            || ids.Distinct().Count() != ids.Count
            || nodes.Any(i => !IsId((string?)i["_tpl"]))
        )
        {
            result.Add(path, "Invalid or duplicate item tree identities.");
            return;
        }
        var roots = nodes.Where(i => !ids.Contains((string?)i["parentId"])).ToArray();
        if (roots.Length != 1)
        {
            result.Add(path, "Each item payload requires one root.");
            return;
        }
        foreach (var item in nodes)
        {
            var seen = new HashSet<string>();
            var current = item;
            while (current != roots[0])
            {
                if (!seen.Add((string)current["_id"]!))
                {
                    result.Add(path, "Cyclic item tree.");
                    break;
                }
                current = nodes.FirstOrDefault(n => (string?)n["_id"] == (string?)current["parentId"])!;
                if (current == null)
                {
                    result.Add(path, "Detached item tree.");
                    break;
                }
            }
            if ((long?)item["upd"]?["StackObjectsCount"] is < 1 or > 10000000)
            {
                result.Add(path, "Invalid item stack quantity.");
            }
        }
    }
}
