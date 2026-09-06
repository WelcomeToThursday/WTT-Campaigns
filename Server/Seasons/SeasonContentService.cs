using Newtonsoft.Json.Linq;
using SeasonalPerks.Server.Hub;
using SeasonalPerks.Shared.Effects;
using SeasonalPerks.Shared.Seasons;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Locales;
using SPTarkov.Server.Core.Utils;
using Path = System.IO.Path;

namespace SeasonalPerks.Server.Seasons;

public sealed record ContentChoice(string Id, string Name);

[Injectable(InjectionType.Singleton, OnLoadOrder.Preload + 90000)]
public sealed class SeasonContentService(
    SeasonRepository repository,
    TemplateTable templates,
    TradersTable traders,
    InventoryConfig inventory,
    LocaleService locales,
    LocaleTable localeTable,
    JsonUtil json
) : IOnLoad
{
    public bool Ready { get; private set; }
    private readonly Dictionary<string, string> _owners = new();

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        // Keep legacy templates available to old profiles even when another season is active.
        Register(repository.Legacy, false);
        var selected = repository.Selection.Active;
        foreach (
            var pack in repository
                .Packs()
                .Where(p => p.Key == selected || p.Key == repository.Selection.Pending || repository.IsUsed(p.Manifest.SeasonId))
        )
        {
            try
            {
                var definition = repository.Pack(pack.Key);
                if (SeasonValidator.Validate(definition).CanPublish)
                {
                    Register(definition, false);
                }
            }
            catch (Exception)
            { /* Unselected packs do not prevent the creator opening for repair. */
            }
        }
        return Task.CompletedTask;
    }

    public void CompleteActivation()
    {
        var selected = repository.Selection.Active;
        var candidate = repository.Selection.Pending ?? selected;
        try
        {
            var definition = repository.Pack(candidate);
            var validation = Validate(definition);
            if (candidate != "legacy" && !validation.CanActivate)
            {
                throw new InvalidDataException(
                    string.Join("; ", validation.Issues.Where(i => i.Severity != "warning").Select(i => i.Message).Take(8))
                );
            }

            repository.CheckGameplay(definition);
            Register(definition, true);
            repository.Activate(candidate);
        }
        catch (Exception e)
        {
            try
            {
                var previous = repository.Pack(selected);
                if (selected != "legacy" && !Validate(previous).CanActivate)
                {
                    throw new InvalidDataException("Previous pack failed validation.");
                }
                Register(previous, true);
                repository.Activate(selected);
            }
            catch
            {
                Register(repository.Legacy, true);
                repository.Activate("legacy");
            }
            repository.ActivationFailed(e.Message);
        }
        Ready = true;
    }

    public List<ContentChoice> Choices(string kind, string search = "")
    {
        var locale = locales.GetLocaleDb("en");
        string Name(string id, string fallback)
        {
            return locale.GetValueOrDefault(id + " Name") ?? locale.GetValueOrDefault(id + " name") ?? fallback;
        }

        IEnumerable<ContentChoice> choices = kind switch
        {
            "items" => templates
                .Items.Values.Where(i => i.Type == "Item")
                .Select(i => new ContentChoice(i.Id.ToString(), Name(i.Id.ToString(), i.Name ?? i.Id.ToString()))),
            "quests" => templates.Quests.Values.Select(q => new ContentChoice(q.Id.ToString(), Name(q.Id.ToString(), q.Id.ToString()))),
            "traders" => traders.Keys.Select(t => new ContentChoice(t.ToString(), Name(t.ToString(), t.ToString()))),
            "customizations" => templates
                .Customization.Values.Where(c => HubGameplay.CustomizationKind(c.Parent) != null)
                .Select(c => new ContentChoice(c.Id.ToString(), Name(c.Id.ToString(), c.Name ?? c.Id.ToString()))),
            "presets" => templates.Profiles.Keys.Select(p => new ContentChoice(p, p)),
            "skills" => Enum.GetNames<SkillTypes>().Select(p => new ContentChoice(p, p)),
            "offers" => traders.SelectMany(t =>
                t.Value.Assort?.Items.Where(i => t.Value.Assort.BarterScheme.ContainsKey(i.Id))
                    .Select(i => new ContentChoice(
                        t.Key + "/" + i.Id,
                        Name(i.Template.ToString(), i.Template.ToString()) + " — " + Name(t.Key.ToString(), t.Key.ToString())
                    ))
                ?? []
            ),
            "categories" => templates
                .Items.Values.Where(i => i.Type != "Item")
                .Select(i => new ContentChoice(i.Id.ToString(), i.Name ?? i.Id.ToString())),
            _ => [],
        };
        return choices
            .Where(c =>
                string.IsNullOrEmpty(search)
                || c.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || c.Id.Contains(search, StringComparison.OrdinalIgnoreCase)
            )
            .OrderBy(c => c.Name)
            .Take(150)
            .ToList();
    }

    public JObject Offer(string composite)
    {
        var parts = composite.Split('/');
        var trader = traders[new MongoId(parts[0])];
        var root = new MongoId(parts[1]);
        var keep = new HashSet<string> { root.ToString() };
        bool changed;
        do
        {
            changed = false;
            foreach (var item in trader.Assort.Items)
            {
                if (item.ParentId != null && keep.Contains(item.ParentId))
                {
                    changed |= keep.Add(item.Id.ToString());
                }
            }
        } while (changed);
        return new JObject
        {
            ["id"] = SeasonRepository.NewId(),
            ["type"] = "AssortmentUnlock",
            ["target"] = root.ToString(),
            ["traderId"] = parts[0],
            ["loyaltyLevel"] = trader.Assort.LoyalLevelItems[root],
            ["items"] = JArray.Parse(json.Serialize(trader.Assort.Items.Where(i => keep.Contains(i.Id.ToString())))!),
        };
    }

    public SeasonValidationResult Validate(SeasonDefinition definition)
    {
        var result = SeasonValidator.Validate(definition);
        if (!result.CanPublish)
        {
            return result;
        }

        try
        {
            var available = templates
                .Items.Keys.Select(i => i.ToString())
                .Concat(definition.Items.Select(i => i.Id))
                .Concat(definition.ImportedItems.Properties().Select(p => p.Name))
                .ToHashSet();
            void Item(string? id, string path)
            {
                if (id == null || !available.Contains(id))
                {
                    result.Add(path, "Missing item dependency: " + id, "dependency");
                }
            }
            foreach (var item in definition.Items)
            {
                Item(item.CloneFrom, "Items/" + item.Id);
                if (
                    templates.Items.ContainsKey(new MongoId(item.Id))
                    && (!_owners.TryGetValue(item.Id, out var owner) || owner != definition.Id)
                )
                {
                    result.Add("Items/" + item.Id, "Owned item collides with an installed template.");
                }
            }
            TemplateItem? Source(string id)
            {
                var seen = new HashSet<string>();
                while (seen.Add(id))
                {
                    var clone = definition.Items.FirstOrDefault(i => i.Id == id);
                    if (clone == null)
                    {
                        return templates.Items.GetValueOrDefault(new MongoId(id));
                    }
                    id = clone.CloneFrom;
                }
                return null;
            }
            foreach (var document in definition.Documents)
            {
                Item(document.ItemId, "Documents");
                var source = Source(document.ItemId);
                if (
                    source != null
                    && (
                        source.Properties?.QuestItem == true
                        || source.Properties?.Slots?.Count() > 0
                        || source.Properties?.Grids?.Count() > 0
                    )
                )
                {
                    result.Add("Documents", document.Name + " needs an ordinary inventory item without equipment slots or storage grids.");
                }
            }
            foreach (var crate in definition.Crates)
            {
                var source = Source(crate.ItemId);
                if (source != null && source.Parent.ToString() != "62f109593b54472778797866")
                {
                    result.Add("Exchanges", "Choose a native loot-container model for this crate.");
                }

                if (!definition.Items.Any(i => i.Id == crate.ItemId) && !definition.ImportedItems.ContainsKey(crate.ItemId))
                {
                    result.Add("Exchanges", "Create a season-owned crate item before defining its contents.");
                }
            }
            foreach (var crate in definition.Crates)
            {
                Item(crate.ItemId, "Exchanges");
                foreach (var id in crate.Pool.Keys)
                {
                    Item(id, "Exchanges");
                }
            }
            if (definition.ExchangeCrate.Length > 0)
            {
                Item(definition.ExchangeCrate, "Exchanges");
            }

            var questIds = templates
                .Quests.Keys.Select(i => i.ToString())
                .Concat(definition.Quests.Where(q => (bool?)q["_seasonalEnabled"] != false).Select(q => (string)q["_id"]!))
                .ToHashSet();
            foreach (var reward in definition.AllRewards.Where(r => r.Enabled))
            {
                var path = "Rewards/" + reward.Id;
                foreach (var grant in reward.Grants)
                {
                    foreach (var item in grant["items"] ?? new JArray())
                    {
                        Item((string?)item["_tpl"], path);
                    }

                    if (
                        (string?)grant["type"] == "CustomizationDirect"
                        && (
                            !templates.Customization.TryGetValue(new MongoId((string)grant["target"]!), out var custom)
                            || HubGameplay.CustomizationKind(custom.Parent) == null
                        )
                    )
                    {
                        result.Add(path, "Customization is not installed.", "dependency");
                    }

                    if ((string?)grant["type"] == "AssortmentUnlock")
                    {
                        if (!traders.TryGetValue(new MongoId((string)grant["traderId"]!), out var trader) || trader.Assort == null)
                        {
                            result.Add(path, "Trader is not installed.", "dependency");
                        }
                        else
                        {
                            var items = json.Deserialize<List<Item>>(grant["items"]!.ToString())!;
                            var target = (string)grant["target"]!;
                            var root = items.FirstOrDefault(i => i.Id.ToString() == target);
                            if (root == null)
                            {
                                result.Add(path, "Offer target must be the item-tree root.");
                            }
                            else if (
                                trader.Assort.Items.Count(i =>
                                    i.Template == root.Template
                                    && trader.Assort.BarterScheme.ContainsKey(i.Id)
                                    && trader.Assort.LoyalLevelItems.GetValueOrDefault(i.Id) == (int)grant["loyaltyLevel"]!
                                    && HubGameplay.Signature(items, target) == HubGameplay.Signature(trader.Assort.Items, i.Id.ToString())
                                ) != 1
                                && !definition.Offers.Any(o => (string?)o["Target"] == target)
                            )
                            {
                                result.Add(path, "Trader offer is missing or ambiguous. Select an installed offer again.", "dependency");
                            }
                        }
                    }
                }
                foreach (var condition in reward.Conditions.Where(c => (string?)c["conditionType"] == "Quest"))
                {
                    if (!questIds.Contains((string)condition["target"]!))
                    {
                        result.Add(path, "Required quest is not installed.", "dependency");
                    }
                }
            }
            foreach (var quest in definition.Quests.OfType<JObject>())
            {
                if ((bool?)quest["_seasonalEnabled"] == false)
                {
                    continue;
                }

                var path = "Quests/" + (string?)quest["_id"];
                if (!definition.Legacy)
                {
                    foreach (var field in new[] { "startedMessageText", "successMessageText", "description", "name" })
                    {
                        if (string.IsNullOrEmpty((string?)quest[field]))
                        {
                            result.Add(path, "Quest is missing its " + field + " localization reference.");
                        }
                    }

                    try
                    {
                        var native = (JObject)quest.DeepClone();
                        native.Remove("localization");
                        _ = json.Deserialize<Quest>(native.ToString());
                    }
                    catch (System.Text.Json.JsonException e)
                    {
                        result.Add(path, "Quest does not match the installed SPT contract: " + e.Message);
                    }
                }
                if (!traders.ContainsKey(new MongoId((string)quest["traderId"]!)))
                {
                    result.Add(path, "Quest trader is not installed.", "dependency");
                }

                foreach (var item in quest.Descendants().OfType<JObject>().Where(o => o["_tpl"] != null))
                {
                    Item((string?)item["_tpl"], path);
                }

                foreach (var c in quest.Descendants().OfType<JObject>().Where(o => o["conditionType"] != null))
                {
                    var kind = (string?)c["conditionType"];
                    var targets = c["target"] is JArray a ? a.Values<string>() : new[] { (string?)c["target"] };
                    foreach (var target in targets.Where(t => t != null))
                    {
                        if (kind == "Quest" && !questIds.Contains(target!))
                        {
                            result.Add(path, "Quest prerequisite is missing: " + target, "dependency");
                        }

                        if (kind is "FindItem" or "HandoverItem")
                        {
                            Item(target, path);
                            if (
                                SeasonValidator.IsId(target)
                                && templates.Items.TryGetValue(new MongoId(target!), out var item)
                                && item.Properties?.QuestItem == true
                            )
                            {
                                result.Add(path, "Quest-item placements are unsupported. Use ordinary inventory items.");
                            }
                        }
                    }
                }
            }
            if (definition.Starting.Preset.Length > 0 && !templates.Profiles.ContainsKey(definition.Starting.Preset))
            {
                result.Add("Starting character", "Starter preset is not installed.", "dependency");
            }

            foreach (var faction in new[] { definition.Starting.Usec, definition.Starting.Bear })
            {
                foreach (var item in faction.Items)
                {
                    Item(item.Template, "Starting character");
                    if (item.Slot.Length > 0)
                    {
                        var edition = definition.Starting.Preset.Length > 0 ? definition.Starting.Preset : "Standard";
                        if (templates.Profiles.TryGetValue(edition, out var preset))
                        {
                            var setup = JObject
                                .Parse(json.Serialize(preset)!)[ReferenceEquals(faction, definition.Starting.Bear) ? "bear" : "usec"]
                                ?["character"]?["Inventory"];
                            var root = setup?["items"]?.FirstOrDefault(i => (string?)i["_id"] == (string?)setup["equipment"]);
                            if (root != null && templates.Items.TryGetValue(new MongoId((string)root["_tpl"]!), out var equipment))
                            {
                                var slot = JObject
                                    .Parse(json.Serialize(equipment)!)["_props"]
                                    ?["Slots"]?.FirstOrDefault(x => (string?)x["_name"] == item.Slot);
                                var allowed =
                                    slot?["_props"]?["filters"]?.SelectMany(f => f["Filter"] ?? new JArray()).Values<string>().ToHashSet()
                                    ?? [];
                                var ancestors = new HashSet<string>();
                                var source = Source(item.Template);
                                while (source != null && ancestors.Add(source.Id.ToString()))
                                {
                                    source = templates.Items.GetValueOrDefault(source.Parent);
                                }

                                if (slot == null || allowed.Count > 0 && !allowed.Overlaps(ancestors))
                                {
                                    result.Add("Starting character", "Item does not fit equipment slot " + item.Slot + ".");
                                }
                            }
                        }
                    }
                }
                foreach (var skill in faction.Skills.Keys)
                {
                    if (!Enum.TryParse<SkillTypes>(skill, out _))
                    {
                        result.Add("Starting character", "Unknown skill: " + skill);
                    }
                }
            }
            foreach (var perk in definition.Perks.All.Where(p => p.Enabled))
            {
                foreach (var effect in perk.Effects)
                {
                    foreach (var skill in effect.SkillIds ?? [])
                    {
                        if (!Enum.TryParse<SkillTypes>(skill, out _))
                        {
                            result.Add("Perks/" + perk.Id, "Unknown skill: " + skill);
                        }
                    }

                    foreach (var trader in effect.TraderIds ?? [])
                    {
                        if (!SeasonValidator.IsId(trader) || !traders.ContainsKey(new MongoId(trader)))
                        {
                            result.Add("Perks/" + perk.Id, "Trader is not installed: " + trader, "dependency");
                        }
                    }
                }
            }

            foreach (var asset in SeasonCompiler.Assets(definition))
            {
                if (
                    SeasonValidator.IsId(asset)
                        ? repository.AssetPath(asset) == null
                        : !repository.Legacy.Slides.Any(slide => slide.Image == asset)
                )
                {
                    result.Add("Assets", "Artwork is missing or invalid: " + asset);
                }
            }

            foreach (var dependency in definition.Dependencies)
            {
                var parts = dependency.Split(':', 2);
                var present =
                    parts.Length == 2
                    && (
                        parts[0] switch
                        {
                            "item" => available.Contains(parts[1]),
                            "quest" => questIds.Contains(parts[1]),
                            "trader" => SeasonValidator.IsId(parts[1]) && traders.ContainsKey(new MongoId(parts[1])),
                            "preset" => templates.Profiles.ContainsKey(parts[1]),
                            _ => false,
                        }
                    );
                if (!present)
                {
                    result.Add("Overview", "Required external dependency is unavailable: " + dependency, "dependency");
                }
            }
            repository.CheckGameplay(definition);
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or FormatException or NullReferenceException)
        {
            result.Add("Overview", e.Message);
        }
        return result;
    }

    private void Register(SeasonDefinition definition, bool crates)
    {
        // Stage every template first; never leave half a season in the shared database on validation failure.
        var staged = new Dictionary<MongoId, TemplateItem>();
        foreach (var pair in definition.ImportedItems.Properties())
        {
            var id = new MongoId(pair.Name);
            if (templates.Items.ContainsKey(id))
            {
                _owners.TryAdd(pair.Name, definition.Id);
                continue;
            }
            var item = (JObject)pair.Value.DeepClone();
            var bundle = (string)item["_props"]!["Prefab"]!["path"]!;
            if (!bundle.StartsWith("wtt-seasonal/", StringComparison.Ordinal))
            {
                bundle = "wtt-seasonal/" + bundle;
            }

            if (!File.Exists(Path.Combine(repository.ModDirectory, "bundles", bundle)))
            {
                continue;
            }

            item["_props"]!["Prefab"]!["path"] = bundle;
            if ((string?)item["_parent"] == "6a28212a0368f4438b0d0a45")
            {
                item["_parent"] = "5448ecbe4bdc2d60728b4568";
            }

            staged[id] = json.Deserialize<TemplateItem>(item.ToString())!;
        }
        var remaining = definition.Items.Where(i => !templates.Items.ContainsKey(new MongoId(i.Id))).ToList();
        while (remaining.Count > 0)
        {
            var ready = remaining
                .Where(i => staged.ContainsKey(new MongoId(i.CloneFrom)) || templates.Items.ContainsKey(new MongoId(i.CloneFrom)))
                .ToArray();
            if (ready.Length == 0)
            {
                if (crates)
                {
                    throw new InvalidDataException("Missing or cyclic item-model dependency.");
                }
                break;
            }
            foreach (var item in ready)
            {
                var original = staged.GetValueOrDefault(new MongoId(item.CloneFrom)) ?? templates.Items[new MongoId(item.CloneFrom)];
                var node = JObject.Parse(json.Serialize(original)!);
                var ids = node.Descendants()
                    .OfType<JProperty>()
                    .Where(p => p.Name == "_id" && SeasonValidator.IsId((string?)p.Value))
                    .Select(p => (string)p.Value!)
                    .Distinct()
                    .ToDictionary(
                        id => id,
                        id => SeasonRepository.Hash(System.Text.Encoding.UTF8.GetBytes(item.Id + ":" + id)).Substring(0, 24)
                    );
                ids[original.Id.ToString()] = item.Id;
                foreach (var value in node.Descendants().OfType<JValue>().Where(v => v.Type == JTokenType.String).ToArray())
                {
                    if (ids.TryGetValue((string)value!, out var replacement))
                    {
                        value.Value = replacement;
                    }
                }

                node["_id"] = item.Id;
                node["_name"] = item.Name;
                node["_props"]!["Width"] = item.Width;
                node["_props"]!["Height"] = item.Height;
                node["_props"]!["StackMaxSize"] = item.StackMax;
                staged[new MongoId(item.Id)] = json.Deserialize<TemplateItem>(node.ToString())!;
                remaining.Remove(item);
            }
        }
        foreach (var item in staged)
        {
            templates.Items[item.Key] = item.Value;
            _owners[item.Key.ToString()] = definition.Id;
            if (!templates.Handbook.Items.Any(i => i.Id == item.Key))
            {
                templates.Handbook.Items.Add(
                    new HandbookItem
                    {
                        Id = item.Key,
                        ParentId = new MongoId("5b47574386f77428ca22b341"),
                        Price = 0,
                    }
                );
            }
        }
        foreach (var language in localeTable.Global.Keys)
        {
            foreach (var text in SeasonCompiler.Texts(definition, language))
            {
                locales.GetLocaleDb(language)[text.Key] = text.Value;
            }
        }

        if (crates)
        {
            foreach (var crate in definition.Crates)
            {
                inventory.RandomLootContainers[new MongoId(crate.ItemId)] = new RewardDetails
                {
                    Type = "RandomLootContainer",
                    RewardCount = crate.RewardCount,
                    FoundInRaid = crate.FoundInRaid,
                    RewardTplPool = crate.Pool.ToDictionary(p => new MongoId(p.Key), p => p.Value),
                    RewardTypePool = [],
                };
            }
        }
    }
}
