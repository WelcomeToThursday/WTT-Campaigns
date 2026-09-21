using WTT.Campaigns.Shared.Effects;
using WTT.Campaigns.Shared.Native;
using WTT.Campaigns.Shared.Serialization;
using WTT.Campaigns.Shared.Story;

namespace WTT.Campaigns.Shared.Seasons;

public static class SeasonValidator
{
    public static bool SupportsQuestReward(string type) =>
        type
            is "Item"
                or "Experience"
                or "TraderStanding"
                or "TraderUnlock"
                or "AssortmentUnlock"
                or "TraderStandingRestore"
                or "Skill"
                or "Customization"
                or "ProductionScheme";

    public static bool IsId(string? id)
    {
        return id != null && id.Length == 24 && id.AsValueEnumerable().All(c => "0123456789abcdef".Contains(c));
    }

    public static SeasonValidationResult Validate(SeasonDefinition season)
    {
        var result = new SeasonValidationResult();
        try
        {
            if (season.MissionPackage != null)
            {
                Missions.MissionLibrary.ValidatePackage(season, result);
                return result;
            }
            Missions.MissionLibrary.ValidateLinks(season, result);
            ValidateCore(season, result);
            foreach (var error in Spatial.SpatialRules.Errors(season))
            {
                result.Add("Zones", error);
            }

            Story.StoryValidator.Validate(season, result);
            CampaignQuestResources.Validate(season, result);
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
        Need(
            s.FormatVersion is 1 or 2 or 3 or 4 or 5 or 6 or 7 or 8 or 9 or 10 or 11 or 12 or 13 or 14,
            "Overview",
            "Unsupported campaign format version."
        );
        foreach (var zone in s.Zones)
        {
            if (
                !string.IsNullOrEmpty(zone.LayoutId)
                && Spatial.SpatialRules.Uses(s, zone.Id).AsValueEnumerable().Any()
                && !MissionOwnsZoneReferences(s, zone)
                && !s
                    .MapLayouts.AsValueEnumerable()
                    .Any(l =>
                        l.Id == zone.LayoutId
                        && l.ApplyInNormalRaids
                        && Authoring.EditorContentRules.Mode(s, l.Id) == Authoring.EditorContentMode.Level
                    )
            )
            {
                r.Add("Zones/" + zone.Id, "Quest/story zones require an enabled ordinary-raid level or their owning mission: " + zone.Id);
            }
        }
        TraderOfferRules.Validate(s, r);
        if (s.MapLayouts.Count > 0 && s.FormatVersion < Spatial.MapLayoutRules.Format(s.MapLayouts))
            r.Add(
                "Maps",
                "Map layouts require format 4; loot and container edits require format 5; AI encounters require format 6; game asset placements require format 8; route splines require format 12; navigation recipes require format 13; terrain painting requires format 14."
            );
        if (s.Missions.Count > 0 && s.FormatVersion < 7)
            r.Add("Missions", "Mission definitions require campaign format 7.");
        if (
            s.MapLayouts.Count > 128
            || s.MapLayouts.AsValueEnumerable()
                .SelectMany(Spatial.MapLayoutRules.OwnedIds)
                .GroupBy(x => x)
                .Any(g => g.AsValueEnumerable().Count() > 1)
        )
            r.Add("Maps", "Layouts require unique identities (at most 128 layouts).");
        foreach (var layout in s.MapLayouts)
        foreach (var error in Spatial.MapLayoutRules.Errors(layout))
            r.Add("Maps/" + layout.Name, error);
        foreach (var error in Spatial.MapLayerRules.Errors(s.MapLayouts))
            r.Add("Maps", error);
        Need(IsId(s.Id) && IsId(s.BattlePassId), "Overview", "Campaign and battle pass require valid identities.");
        Need(!string.IsNullOrWhiteSpace(s.Name) && s.Name.Length <= 120, "Overview", "Name is required (up to 120 characters).");
        Need(s.Rules.StartingPoints is >= 0 and <= 100000, "Perks", "Starting budget must be 0–100000.");
        Need(IsId(s.UniversalImage) && IsId(s.UniversalUnavailableImage), "Assets", "Choose both Classified document images.");
        Need(s.Locales.ContainsKey("en"), "Localization", "English fallback is required.");
        Need(s.Documents.Count is >= 1 and <= 8, "Documents", "Choose between one and eight document types.");
        Need(
            s.Documents.AsValueEnumerable().Select(d => d.ItemId).Distinct().Count() == s.Documents.Count,
            "Documents",
            "Each document requires a different item template."
        );
        Need(
            s.Collection.DocumentsPerRaid is >= 0 and <= 8
                && s.Collection.MapCounts.Values.AsValueEnumerable().All(n => n is >= 0 and <= 8),
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
        if (
            s.Items.Count > 1000
            || s.Quests.Count > 1000
            || s.Perks.All.AsValueEnumerable().Count() > 2000
            || s.AllRewards.AsValueEnumerable().Count() > 610
        )
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
        foreach (var item in s.ImportedItems)
        {
            Identity(item.Key, "Items");
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
                next = s.Items.AsValueEnumerable().FirstOrDefault(i => i.Id == next.CloneFrom);
            }
        }
        foreach (var perk in s.Perks.All)
        {
            var path = "Perks/" + perk.Id;
            Identity(perk.Id, path);
            Need(IsId(perk.ImageUrl), path, "Choose a perk icon.");
            var unavailable = EffectSupport.UnavailableReason(perk);
            if (unavailable != null && perk.Enabled)
            {
                r.Add(path, unavailable, s.Legacy || !perk.Enabled ? "warning" : "error");
            }

            Need(
                perk.Conflicts.AsValueEnumerable().All(id => id != perk.Id && s.Perks.Personal.AsValueEnumerable().Any(p => p.Id == id)),
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
            s.Rules.EnabledCommonIds.AsValueEnumerable()
                .All(id => s.Perks.Common.AsValueEnumerable().Any(p => p.Id == id && EffectSupport.UnavailableReason(p) == null)),
            "Perks",
            "Common rules include an unknown or unsupported perk."
        );
        Need(s.Pages.Count is >= 1 and <= 100, "Battle pass", "Use 1–100 battle pass pages.");
        for (var page = 0; page < s.Pages.Count; page++)
        {
            var p = s.Pages[page];
            Need(
                p.PreviousRequirement >= 0
                    && p.PreviousRequirement <= (page == 0 ? 0 : s.Pages[page - 1].Rewards.AsValueEnumerable().Count(x => x.Enabled)),
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
                reward.Costs.AsValueEnumerable().Select(c => c.DocumentId).Distinct().Count() == reward.Costs.Count,
                path,
                "Combine duplicate document costs."
            );
            Need(
                reward
                    .Costs.AsValueEnumerable()
                    .All(c => c.Count is >= 1 and <= 100000 && s.Documents.AsValueEnumerable().Any(d => d.Id == c.DocumentId)),
                path,
                "Costs require known documents and positive quantities."
            );
            if (!reward.Enabled)
            {
                continue;
            }

            Need(IsId(reward.Image) && IsId(reward.BigImage), path, "Choose thumbnail and full artwork.");
            Need(
                reward.Grants.AsValueEnumerable().All(g => g != null) && reward.Conditions.AsValueEnumerable().All(c => c != null),
                path,
                "Malformed payload or condition."
            );
            Need(reward.Grants.Count > 0, path, "Add at least one reward payload.");
            foreach (var grant in reward.Grants)
            {
                var kind = (string?)grant.Type;
                Need(kind is "Item" or "Tarcoin" or "CustomizationDirect" or "AssortmentUnlock", path, "Unsupported reward type: " + kind);
                if (kind == "Tarcoin")
                {
                    Need((long?)grant.Value is >= 1 and <= 1000000000, path, "Tarcoin amount must be positive and at most one billion.");
                }

                if (kind is "CustomizationDirect" or "AssortmentUnlock")
                {
                    Need(IsId((string?)grant.Target), path, "Select a reward target.");
                }

                if (kind == "Item" || (kind == "AssortmentUnlock" && !s.TraderOffers.AsValueEnumerable().Any(o => o.Id == grant.Target)))
                {
                    ItemTree(grant.Items, path, r);
                }

                if (kind == "AssortmentUnlock")
                {
                    Need(
                        IsId((string?)grant.TraderId) && (int?)grant.LoyaltyLevel is >= 1 and <= 4,
                        path,
                        "Select a trader and loyalty level 1–4."
                    );
                }
            }
            foreach (var condition in reward.Conditions)
            {
                var kind = (string?)condition.ConditionType;
                Need(kind is "Level" or "Quest", path, "Reward requirements support level and completed quests.");
                if (kind == "Level")
                {
                    Need((int?)condition.Value is >= 1 and <= 100, path, "Level requirement must be 1–100.");
                }

                if (kind == "Quest")
                {
                    Need(IsId((string?)condition.Target), path, "Select a required quest.");
                }

                if (kind == "Level")
                {
                    Need(
                        condition.CompareMethod == null || (string?)condition.CompareMethod == ">=",
                        path,
                        "Level requirements use minimum levels."
                    );
                }
            }
        }
        Need(
            s.Crates.AsValueEnumerable().Select(c => c.ItemId).Distinct().Count() == s.Crates.Count,
            "Exchanges",
            "Crate definitions must have unique item templates."
        );
        foreach (var crate in s.Crates)
        {
            Need(
                IsId(crate.ItemId)
                    && crate.RewardCount is >= 1 and <= 100
                    && crate.Pool.Count > 0
                    && crate.Pool.AsValueEnumerable().All(p => IsId(p.Key) && p.Value > 0 && !double.IsInfinity(p.Value)),
                "Exchanges",
                "Crates require a template, 1–100 rewards, and positive finite item weights."
            );
        }

        var quests = s.Quests.AsValueEnumerable().ToDictionary(q => q.Id);
        var disabledQuests = s.Quests.AsValueEnumerable().Count(q => q.SeasonalEnabled == false);
        if (disabledQuests > 0)
        {
            r.Add(
                "Quests",
                $"{disabledQuests} quests are disabled and will not be playable. They are retained for editing; enable them only after resolving their compatibility and prerequisites.",
                "info"
            );
        }
        foreach (var quest in quests)
        {
            var storyQuest = s.Story?.Quests.AsValueEnumerable().Any(q => q.QuestId == quest.Key) == true;
            Identity(quest.Key, "Quests/" + quest.Key);
            if ((bool?)quest.Value.SeasonalEnabled == false)
            {
                continue;
            }

            foreach (var condition in quest.Value.AllConditions())
            {
                var kind = (string?)condition.ConditionType;
                if (
                    storyQuest
                        ? !Story.StoryQuestCompatibility.Supports(quest.Value, condition)
                        : kind is not ("Quest" or "Level" or "TraderLoyalty" or "FindItem" or "HandoverItem")
                )
                {
                    r.Add("Quests/" + quest.Key, "Unsupported objective: " + kind, s.Legacy ? "warning" : "error");
                }
            }
            if (!s.Legacy)
            {
                var path = "Quests/" + quest.Key;
                Need(
                    quest.Value.Conditions != null && quest.Value.Rewards != null && quest.Value.Localization.ContainsKey("en"),
                    path,
                    "Quest requires conditions, rewards, and English text."
                );
                foreach (var stage in new[] { "AvailableForStart", "AvailableForFinish", "Fail" })
                {
                    Need(quest.Value.Conditions?.Stage(stage) != null, path, "Missing quest stage: " + stage);
                }

                foreach (var condition in quest.Value.AllConditions())
                {
                    var kind = (string?)condition.ConditionType;
                    Need(IsId((string?)condition.Id), path, "Objective requires an identity.");
                    if (!storyQuest && kind != "Quest")
                    {
                        Need((double?)condition.Value is >= 1 and <= 1000000, path, "Objective requires a positive quantity or level.");
                    }
                    else if (condition.Value != null)
                    {
                        var value = (double)condition.Value!;
                        Need(
                            !double.IsNaN(value) && !double.IsInfinity(value) && Math.Abs(value) <= 1000000,
                            path,
                            "Story objective requires a finite value."
                        );
                    }
                    if (kind is "FindItem" or "HandoverItem")
                    {
                        Need(
                            condition.Target is { IsList: true } targets
                                && targets.Values.Count > 0
                                && targets.AsValueEnumerable().All(IsId),
                            path,
                            "Select objective items."
                        );
                    }

                    if (kind is "Quest" or "TraderLoyalty")
                    {
                        Need(IsId((string?)condition.Target), path, "Select the prerequisite quest or trader.");
                    }

                    if (kind == "Level")
                    {
                        Need((int?)condition.Value is >= 1 and <= 100, path, "Quest level must be 1–100.");
                    }

                    if (kind == "TraderLoyalty")
                    {
                        Need((int?)condition.Value is >= 1 and <= 4, path, "Trader loyalty must be 1–4.");
                    }

                    if (kind == "Quest" && !storyQuest)
                    {
                        Need(
                            condition.Status is { Count: > 0 } statuses
                                && statuses
                                    .AsValueEnumerable()
                                    .All(status =>
                                        status
                                            is "0"
                                                or "1"
                                                or "2"
                                                or "3"
                                                or "4"
                                                or "5"
                                                or "6"
                                                or "7"
                                                or "8"
                                                or "9"
                                                or "Locked"
                                                or "AvailableForStart"
                                                or "Started"
                                                or "AvailableForFinish"
                                                or "Success"
                                                or "Fail"
                                                or "FailRestartable"
                                                or "MarkedAsFailed"
                                                or "Expired"
                                                or "AvailableAfter"
                                    ),
                            path,
                            "Quest conditions require valid quest states."
                        );
                    }
                }
                foreach (var grant in quest.Value.AllRewards())
                {
                    var kind = (string?)grant.Type;
                    Need(SupportsQuestReward(kind!), path, "Unsupported native quest reward: " + kind);
                    if (kind is "Item" or "AssortmentUnlock")
                    {
                        ItemTree(grant.Items, path, r);
                    }
                }
            }
        }
        foreach (var id in QuestDependencyRules.Cycles(s.Quests))
        {
            r.Add("Quests/" + id, "Quest dependency cycle.", s.Legacy ? "warning" : "error");
        }

        ValidateMissions(s, quests, r, Need, Identity);
        foreach (var faction in new[] { s.Starting.Usec, s.Starting.Bear })
        {
            Need(
                faction.Skills.Values.AsValueEnumerable().All(n => n is >= 0 and <= 51),
                "Starting character",
                "Starting skill levels must be 0–51."
            );
            Need(
                faction
                    .Items.AsValueEnumerable()
                    .All(i => IsId(i.Template) && i.Count is >= 1 and <= 10000000 && (i.Slot.Length == 0 || i.Count == 1)),
                "Starting character",
                "Choose valid starter items and quantities; equipment slots hold one item."
            );
            Need(
                faction.Items.AsValueEnumerable().Where(i => i.Slot.Length > 0).Select(i => i.Slot).Distinct().Count()
                    == faction.Items.AsValueEnumerable().Count(i => i.Slot.Length > 0),
                "Starting character",
                "Only one replacement per equipment slot."
            );
        }

        static void ValidateMissions(
            SeasonDefinition s,
            IReadOnlyDictionary<string, NativeQuest> quests,
            SeasonValidationResult result,
            Action<bool, string, string> need,
            Action<string, string> identity
        )
        {
            need(s.Missions.Count <= 1000, "Missions", "Use at most 1000 mission definitions.");
            var layouts = s.MapLayouts.AsValueEnumerable().ToDictionary(l => l.Id);
            var missionIds = new HashSet<string>();
            foreach (var mission in s.Missions)
            {
                var path = "Missions/" + mission.Id;
                identity(mission.Id, path);
                need(missionIds.Add(mission.Id), path, "Mission identities must be unique.");
                need(
                    !string.IsNullOrWhiteSpace(mission.Name) && mission.Name.Length <= 120,
                    path,
                    "Mission name is required (up to 120 characters)."
                );
                need(
                    !string.IsNullOrWhiteSpace(mission.Briefing) && mission.Briefing.Length <= 4000,
                    path,
                    "Mission briefing is required (up to 4000 characters)."
                );
                need(IsId(mission.LayoutId) && layouts.ContainsKey(mission.LayoutId), path, "Choose an existing mission layout.");
                need(IsId(mission.QuestId) && quests.ContainsKey(mission.QuestId), path, "Choose an existing native quest.");
                need(
                    IsId(mission.QuestId) && s.Story?.Quests.AsValueEnumerable().Any(q => q.QuestId == mission.QuestId) == true,
                    path,
                    "Mission quests must belong to the campaign story."
                );

                if (!layouts.TryGetValue(mission.LayoutId, out var layout))
                {
                    continue;
                }

                foreach (
                    var error in Spatial
                        .MapLayoutRules.Errors(layout, walkthrough: true)
                        .AsValueEnumerable()
                        .Concat(WTT.Campaigns.Shared.Missions.MissionLogicRules.Errors(mission, layout))
                )
                {
                    result.Add(path, error);
                }

                need(
                    !WTT.Campaigns.Shared.Missions.MissionLogic.HasLogic(mission) || s.FormatVersion >= 10,
                    path,
                    "Mission events, objectives and retries require campaign format 10."
                );
                if (!quests.TryGetValue(mission.QuestId, out var quest))
                {
                    continue;
                }

                var condition = quest
                    .Conditions?.AvailableForFinish?.AsValueEnumerable()
                    .FirstOrDefault(c => (string?)c.Id == mission.CompletionConditionId);
                need(IsId(mission.CompletionConditionId), path, "Choose the mission completion objective.");
                need(condition != null, path, "Mission completion objective must be in AvailableForFinish.");
                need(condition?.ConditionType == "GlobalVariableValue", path, "Mission completion objective must use GlobalVariableValue.");
                var variableId = condition?.Target?.Values is { Count: 1 } values ? values[0] : "";
                var variable = s.Story?.Variables.AsValueEnumerable().FirstOrDefault(v => v.Id == variableId);
                need(
                    variable != null && variable.Scope == StoryVariableScope.Profile && variable.InitialValue == 0,
                    path,
                    "Mission completion target must be a profile story variable initialized to zero."
                );
                need(condition?.Value is >= 1 and <= 1_000_000, path, "Mission completion objective value must be one or greater.");
                need(
                    condition?.CompareMethod == null || condition.CompareMethod == ">=",
                    path,
                    "Mission completion objective must use a minimum comparison."
                );
            }
        }

        static bool MissionOwnsZoneReferences(SeasonDefinition season, Spatial.SeasonZone zone)
        {
            // A layout-owned zone is available during a mission run only when every
            // quest reference belongs to a mission using that same layout. Story raid
            // bindings have no mission identity and remain ordinary-raid data.
            if (season.Story?.RaidBindings.AsValueEnumerable().Any(binding => binding.ZoneId == zone.Id) == true)
            {
                return false;
            }

            var missionQuestIds = season
                .Missions.AsValueEnumerable()
                .Where(mission => mission.LayoutId == zone.LayoutId && IsId(mission.QuestId))
                .Select(mission => mission.QuestId)
                .ToHashSet(StringComparer.Ordinal);
            if (missionQuestIds.Count == 0)
            {
                return false;
            }

            return season
                .Quests.AsValueEnumerable()
                .Where(quest =>
                    quest
                        .AllConditions()
                        .AsValueEnumerable()
                        .Any(condition => Spatial.SpatialRules.References(condition).AsValueEnumerable().Contains(zone.Id))
                )
                .All(quest => missionQuestIds.Contains(quest.Id));
        }

        void Grid(IEnumerable<SeasonReward> rewards, int columns, int rows, string path)
        {
            var cells = new HashSet<(int, int)>();
            foreach (var tile in rewards.AsValueEnumerable().Where(t => t.Enabled))
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

    public static void ItemTree(List<NativeItem>? items, string path, SeasonValidationResult result)
    {
        if (items == null || items.Count == 0)
        {
            result.Add(path, "An item payload needs an item tree.");
            return;
        }
        var nodes = items.AsValueEnumerable().ToList();
        var ids = nodes.AsValueEnumerable().Select(i => i.Id).ToList();
        if (
            nodes.Count != items.Count
            || ids.AsValueEnumerable().Any(id => !IsId(id))
            || ids.AsValueEnumerable().Distinct().Count() != ids.Count
            || nodes.AsValueEnumerable().Any(i => !IsId((string?)i.Template))
        )
        {
            result.Add(path, "Invalid or duplicate item tree identities.");
            return;
        }
        var roots = nodes.AsValueEnumerable().Where(i => i.ParentId == null || !ids.Contains(i.ParentId)).ToArray();
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
                if (!seen.Add(current.Id))
                {
                    result.Add(path, "Cyclic item tree.");
                    break;
                }
                current = nodes.AsValueEnumerable().FirstOrDefault(n => n.Id == (string?)current.ParentId)!;
                if (current == null)
                {
                    result.Add(path, "Detached item tree.");
                    break;
                }
            }
            if ((long?)item.Upd?.StackObjectsCount is < 1 or > 10000000)
            {
                result.Add(path, "Invalid item stack quantity.");
            }
        }
    }
}
