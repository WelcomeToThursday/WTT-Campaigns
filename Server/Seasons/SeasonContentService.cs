using Newtonsoft.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Hideout;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Locales;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Cloners;
using WTT.Campaigns.Server.Hub;
using WTT.Campaigns.Server.Web.Authoring;
using WTT.Campaigns.Shared.Effects;
using WTT.Campaigns.Shared.Seasons;
using Path = System.IO.Path;

namespace WTT.Campaigns.Server.Seasons;

public sealed record ContentChoice(string Id, string Name);

[Injectable(InjectionType.Singleton, OnLoadOrder.Preload + 90000)]
public sealed partial class SeasonContentService(
    SeasonRepository repository,
    TemplateTable templates,
    TradersTable traders,
    InventoryConfig inventory,
    LocaleService locales,
    LocaleTable localeTable,
    HideoutTable hideout,
    JsonUtil json,
    ICloner cloner,
    IReadOnlyList<SptMod> loadedMods,
    TraderOfferCatalogue offerCatalogue,
    ItemPreviewService itemPreviews
) : IOnLoad
{
    public bool Ready { get; private set; }

    private readonly Dictionary<string, string> _owners = new();
    private SeasonItemBundles _itemBundles = null!;

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        // SPT loads BundleLoader after Preload; inspect the loaded mods' manifests here.
        _itemBundles = new SeasonItemBundles(loadedMods.Select(mod => mod.GetModPath()));
        foreach (var item in repository.Legacy.ImportedItems.Values)
        {
            if (_itemBundles.Resolve(item.Properties.Prefab!.Path!) == null)
            {
                throw new InvalidDataException(
                    "WTT-ContentBackport is missing a required campaign item bundle: " + item.Properties.Prefab.Path
                );
            }
        }

        // Keep legacy templates available to old profiles even when another season is active.
        Register(repository.Legacy, false);
        // Historical owned templates remain readable by inventories; only the chosen
        // revision registers recipes, locales, and gameplay during activation.
        foreach (var pack in repository.Packs().OrderBy(p => p.Manifest.Revision).ThenBy(p => p.Key))
        {
            try
            {
                var definition = repository.Pack(pack.Key);
                if (!SeasonValidator.Validate(definition).CanPublish)
                    continue;
                RegisterHistoricalItems(definition);
            }
            catch (Exception e)
            {
                repository.StorageWarnings.Add("Pack " + pack.Key + ": " + e.Message);
            }
        }

        return Task.CompletedTask;
    }

    public void CompleteActivation()
    {
        var selected = repository.Selection.Active;
        var requested = repository.Selection.Pending ?? selected;
        var activated = false;
        IEnumerable<string> Candidates(string key)
        {
            try
            {
                return repository.RevisionCandidates(key);
            }
            catch (Exception e)
            {
                repository.StorageWarnings.Add("Campaign " + key + ": " + e.Message);
                return [];
            }
        }
        foreach (var candidate in Candidates(requested).Concat(Candidates(selected)).Append("legacy").Distinct())
        {
            try
            {
                var definition = repository.Pack(candidate);
                if (definition.MissionPackage != null)
                    throw new InvalidDataException("A mission package cannot be the default campaign.");
                var validation = Validate(definition);
                if (candidate != "legacy" && !validation.CanActivate)
                    throw new InvalidDataException(
                        string.Join("; ", validation.Issues.Where(i => i.Severity != "warning").Select(i => i.Message).Take(8))
                    );
                Register(definition, true);
                repository.Activate(candidate);
                activated = true;
                break;
            }
            catch (Exception e)
            {
                repository.StorageWarnings.Add("Campaign " + candidate + " unavailable: " + e.Message);
            }
        }
        if (!activated)
            throw new InvalidOperationException("No valid default campaign is available.");

        repository.Playable[repository.Current.Definition.Id] = repository.Current;
        var candidates = repository
            .Packs()
            .GroupBy(p => p.Manifest.SeasonId)
            .SelectMany(g => g.OrderByDescending(p => p.Manifest.Revision).ThenBy(p => p.Key).Select(p => p.Key))
            .Prepend("legacy");
        var registeredMissions = new HashSet<string>();
        foreach (var key in candidates)
        {
            try
            {
                var definition = repository.Pack(key);
                if (definition.MissionPackage != null)
                {
                    if (Validate(definition).CanActivate)
                    {
                        if (registeredMissions.Add(definition.Id))
                            Register(definition, false);
                        repository.PublishedMissions[definition.Id + ":" + definition.Revision] = new SeasonRuntimeSnapshot(definition);
                    }
                    continue;
                }
                if (repository.Playable.ContainsKey(definition.Id))
                {
                    continue;
                }

                if (key != "legacy")
                {
                    var validation = Validate(definition);
                    if (!validation.CanActivate)
                        throw new InvalidDataException(
                            string.Join("; ", validation.Issues.Where(i => i.Severity != "warning").Take(8).Select(i => i.Message))
                        );
                }

                repository.CheckGameplay(definition);
                Register(definition, true);
                repository.Playable[definition.Id] = new SeasonRuntimeSnapshot(definition);
            }
            catch (Exception e)
            {
                repository.StorageWarnings.Add("Campaign " + key + " unavailable: " + e.Message);
            }
        }

        Ready = true;
    }

    public List<ContentChoice> Choices(string kind, string search = "")
    {
        return AllChoices(kind)
            .Where(c =>
                string.IsNullOrEmpty(search)
                || c.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || c.Id.Contains(search, StringComparison.OrdinalIgnoreCase)
            )
            .OrderBy(c => c.Name)
            .Take(150)
            .ToList();
    }

    public List<CampaignTraderOffer> InstalledTraderOffers(string trader)
    {
        var owned = repository.OrdinaryPlayable().SelectMany(p => p.Definition.TraderOffers).Select(o => o.Id).ToHashSet();
        return offerCatalogue.InstalledOffers(trader).Where(o => !owned.Contains(o.Id)).ToList();
    }

    public string Name(string kind, string id)
    {
        return AllChoices(kind).FirstOrDefault(c => c.Id == id)?.Name
            ?? (id.Length == 0 ? "None selected" : "Unresolved reference · " + id);
    }

    private IEnumerable<ContentChoice> AllChoices(string kind)
    {
        var locale = locales.GetLocaleDb("en");
        string Name(string id, string fallback)
        {
            return ReferenceNames.Localized("items", id, fallback, key => locale.GetValueOrDefault(key));
        }

        string TraderName(string id)
        {
            var trader = traders.FirstOrDefault(t => t.Key.ToString() == id).Value;
            return ReferenceNames.Localized(
                "traders",
                id,
                trader?.Base.Nickname ?? trader?.Base.Name ?? id,
                key => locale.GetValueOrDefault(key)
            );
        }

        return kind switch
        {
            "items" => templates
                .Items.Values.Where(i => i.Type == "Item")
                .Select(i => new ContentChoice(i.Id.ToString(), Name(i.Id.ToString(), i.Name ?? i.Id.ToString()))),
            "quests" => templates.Quests.Values.Select(q => new ContentChoice(q.Id.ToString(), Name(q.Id.ToString(), q.Id.ToString()))),
            "traders" => traders.Keys.Select(t => new ContentChoice(t.ToString(), TraderName(t.ToString()))),
            "customizations" => templates
                .Customization.Values.Where(c => HubGameplay.CustomizationKind(c.Parent) != null)
                .Select(c => new ContentChoice(c.Id.ToString(), Name(c.Id.ToString(), c.Name ?? c.Id.ToString()))),
            "presets" => templates.Profiles.Keys.Select(p => new ContentChoice(p, p)),
            "skills" => Enum.GetNames<SkillTypes>().Select(p => new ContentChoice(p, p)),
            "crafts" => (hideout.Production.Recipes ?? []).Select(r => new ContentChoice(
                r.Id.ToString(),
                Name(r.EndProduct.ToString(), r.EndProduct.ToString()) + " — " + r.AreaType
            )),
            "offers" => traders.SelectMany(t =>
                t.Value.Assort?.Items.Where(i => t.Value.Assort.BarterScheme.ContainsKey(i.Id))
                    .Select(i => new ContentChoice(
                        t.Key + "/" + i.Id,
                        Name(i.Template.ToString(), i.Template.ToString()) + " — " + TraderName(t.Key.ToString())
                    ))
                ?? []
            ),
            "categories" => templates
                .Items.Values.Where(i => i.Type != "Item")
                .Select(i => new ContentChoice(i.Id.ToString(), Name(i.Id.ToString(), i.Name ?? i.Id.ToString()))),
            _ => [],
        };
    }

    public NativeReward Offer(string composite)
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
        return new NativeReward
        {
            Id = SeasonRepository.NewId(),
            Type = "AssortmentUnlock",
            Target = root.ToString(),
            TraderId = parts[0],
            LoyaltyLevel = trader.Assort.LoyalLevelItems[root],
            Items = JsonConvert.DeserializeObject<List<NativeItem>>(
                json.Serialize(trader.Assort.Items.Where(i => keep.Contains(i.Id.ToString())))!
            )!,
        };
    }

    public SeasonValidationResult Validate(SeasonDefinition definition)
    {
        _ = itemPreviews;
        var result = SeasonValidator.Validate(definition);
        if (!result.CanPublish)
        {
            return result;
        }

        try
        {
            foreach (var link in definition.MissionLinks)
            foreach (var issue in Validate(link.Package).Issues)
                result.Add("MissionLinks/" + link.Id + "/" + issue.Path, issue.Message, issue.Severity);
            foreach (var assort in definition.TraderAssorts)
            {
                if (!offerCatalogue.HasTrader(assort.TraderId))
                    result.Add("Trader offers/" + assort.TraderId, "Trader is not installed or has no fixed assortment.", "dependency");
                else if (!assort.ReplaceExisting)
                    foreach (var removed in assort.RemovedOffers)
                        if (!offerCatalogue.HasOffer(assort.TraderId, removed))
                            result.Add(
                                "Trader offers/" + assort.TraderId,
                                "An installed offer being replaced or removed is no longer present. Restore the installed assortment and reapply this edit: "
                                    + removed,
                                "dependency"
                            );
            }
            foreach (var offer in definition.TraderOffers)
            {
                var preview = itemPreviews.Get(offer.Items, definition.Id);
                offerCatalogue.Validate(offer, result, preview.Verified ? preview.ItemSizes : null);
            }
            var ownedOffers =
                repository
                    .Playable.GetValueOrDefault(definition.Id)
                    ?.Definition.TraderOffers.SelectMany(o => o.Items)
                    .Select(i => i.Id)
                    .ToHashSet()
                ?? [];
            var installedOfferIds = traders.Values.SelectMany(t => t.Assort?.Items ?? []).Select(i => i.Id.ToString()).ToHashSet();
            var otherCampaignIds = repository
                .OrdinaryPlayable()
                .Where(p => p.Definition.Id != definition.Id)
                .SelectMany(p => p.Definition.TraderOffers)
                .SelectMany(o => o.Items)
                .Select(i => i.Id)
                .ToHashSet();
            foreach (var offer in definition.TraderOffers)
                if (
                    offer.Items.Any(i =>
                        otherCampaignIds.Contains(i.Id) || (installedOfferIds.Contains(i.Id) && !ownedOffers.Contains(i.Id))
                    )
                )
                    result.Add(
                        "Trader offers/" + offer.Id,
                        "Offer identities collide with installed content. Copy it as a new campaign offer."
                    );
            var available = templates
                .Items.Keys.Select(i => i.ToString())
                .Concat(definition.Items.Select(i => i.Id))
                .Concat(definition.ImportedItems.Keys)
                .Concat(definition.MissionLinks.SelectMany(l => l.Package.Items.Select(i => i.Id).Concat(l.Package.ImportedItems.Keys)))
                .ToHashSet();
            void Item(string? id, string path)
            {
                if (id == null || !available.Contains(id))
                {
                    result.Add(path, "Missing item dependency: " + id, "dependency");
                }
            }

            foreach (var zone in definition.Zones.Where(z => z.Uses.Contains("Salvage")))
            {
                Item(zone.Salvage.RequiredItemTpl, "Zones and captures/" + zone.Id);
                foreach (var reward in zone.Salvage.Rewards)
                {
                    Item(reward.ItemTpl, "Zones and captures/" + zone.Id);
                }
            }

            foreach (var loot in definition.QuestLoot)
                Item(loot.ItemTemplate, "Quest loot/" + loot.QuestId);
            foreach (var craft in definition.Crafts)
            {
                Item(craft.EndProduct, "Crafts/" + craft.Id);
                foreach (var ingredient in craft.Requirements.Where(r => r.Type == "Item"))
                    Item(ingredient.TemplateId, "Crafts/" + craft.Id);
                if (
                    hideout.Production.Recipes.Any(r => r.Id.ToString() == craft.Id)
                    && (!_craftOwners.TryGetValue(craft.Id, out var owner) || owner != definition.Id)
                )
                    result.Add("Crafts/" + craft.Id, "Owned recipe collides with installed content.");
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
                    result.Add("Exchanges", "Create a campaign-owned crate item before defining its contents.");
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
                .Concat(definition.Quests.Where(q => (bool?)q.SeasonalEnabled != false).Select(q => (string)q.Id!))
                .ToHashSet();
            foreach (var reward in definition.AllRewards.Where(r => r.Enabled))
            {
                var path = "Rewards/" + reward.Id;
                foreach (var grant in reward.Grants)
                {
                    foreach (var item in grant.Items)
                    {
                        Item((string?)item.Template, path);
                    }

                    if (
                        (string?)grant.Type == "CustomizationDirect"
                        && (
                            !templates.Customization.TryGetValue(new MongoId((string)grant.Target!), out var custom)
                            || HubGameplay.CustomizationKind(custom.Parent) == null
                        )
                    )
                    {
                        result.Add(path, "Customization is not installed: " + grant.Target, "dependency");
                    }

                    if ((string?)grant.Type == "AssortmentUnlock" && !definition.TraderOffers.Any(o => o.Id == grant.Target))
                    {
                        if (!traders.TryGetValue(new MongoId((string)grant.TraderId!), out var trader) || trader.Assort == null)
                        {
                            result.Add(path, "Trader is not installed.", "dependency");
                        }
                        else
                        {
                            var items = json.Deserialize<List<Item>>(JsonConvert.SerializeObject(grant.Items))!;
                            var target = (string)grant.Target!;
                            var root = items.FirstOrDefault(i => i.Id.ToString() == target);
                            if (root == null)
                            {
                                result.Add(path, "Offer target must be the item-tree root.");
                            }
                            else if (
                                trader.Assort.Items.Count(i =>
                                    i.Template == root.Template
                                    && trader.Assort.BarterScheme.ContainsKey(i.Id)
                                    && trader.Assort.LoyalLevelItems.GetValueOrDefault(i.Id) == (int)grant.LoyaltyLevel!
                                    && HubGameplay.Signature(items, target) == HubGameplay.Signature(trader.Assort.Items, i.Id.ToString())
                                ) != 1
                                && !definition.Offers.Any(o => (string?)o.Target == target)
                            )
                            {
                                result.Add(path, "Trader offer is missing or ambiguous. Select an installed offer again.", "dependency");
                            }
                        }
                    }
                }

                foreach (var condition in reward.Conditions.Where(c => (string?)c.ConditionType == "Quest"))
                {
                    if (!questIds.Contains((string)condition.Target!))
                    {
                        result.Add(path, "Required quest is not installed.", "dependency");
                    }
                }
            }

            foreach (var quest in definition.Quests)
            {
                if ((bool?)quest.SeasonalEnabled == false)
                {
                    continue;
                }

                var path = "Quests/" + (string?)quest.Id;
                if (!definition.Legacy)
                {
                    foreach (var field in new[] { "startedMessageText", "successMessageText", "description", "name" })
                    {
                        if (string.IsNullOrEmpty(NativeQuestAuthoring.LocaleKey(quest, field)))
                        {
                            result.Add(path, "Quest is missing its " + field + " localization reference.");
                        }
                    }

                    try
                    {
                        _ = json.Deserialize<Quest>(JsonConvert.SerializeObject(quest));
                    }
                    catch (System.Text.Json.JsonException e)
                    {
                        result.Add(path, "Quest does not match the installed SPT contract: " + e.Message);
                    }
                }

                if (!traders.ContainsKey(new MongoId((string)quest.TraderId!)))
                {
                    result.Add(path, "Quest trader is not installed.", "dependency");
                }

                foreach (var item in quest.AllItems())
                {
                    Item((string?)item.Template, path);
                }

                foreach (var c in quest.AllConditions())
                {
                    var kind = (string?)c.ConditionType;
                    var targets = c.Target ?? new StringTargets(Array.Empty<string>());
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
                            var setup = (ReferenceEquals(faction, definition.Starting.Bear) ? preset.Bear : preset.Usec)
                                ?.Character
                                ?.Inventory;
                            var root = setup?.Items?.FirstOrDefault(i => i.Id == setup.Equipment);
                            if (root != null && templates.Items.TryGetValue(root.Template, out var equipment))
                            {
                                var slot = equipment.Properties?.Slots?.FirstOrDefault(x => x.Name == item.Slot);
                                var allowed =
                                    slot?.Properties?.Filters?.SelectMany(f => f.Filter ?? []).Select(id => id.ToString()).ToHashSet()
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

            foreach (var dependency in SeasonCompiler.Dependencies(definition))
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
                            "mod" => loadedMods.Any(mod => mod.ModMetadata.ModGuid == parts[1]),
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

    private void RegisterHistoricalItems(SeasonDefinition definition)
    {
        foreach (var link in definition.MissionLinks)
            RegisterHistoricalItems(link.Package);
        var items = new SeasonDefinition
        {
            Id = definition.Id,
            Items = definition.Items,
            ImportedItems = definition.ImportedItems,
            Locales = new(),
        };
        Register(items, false);
    }

    private void Register(SeasonDefinition definition, bool crates)
    {
        foreach (var link in definition.MissionLinks)
            Register(link.Package, false);
        // Stage every template first; never leave half a season in the shared database on validation failure.
        var staged = new Dictionary<MongoId, TemplateItem>();
        foreach (var pair in definition.ImportedItems)
        {
            var id = new MongoId(pair.Key);
            if (templates.Items.ContainsKey(id) && _owners.GetValueOrDefault(pair.Key) != definition.Id)
            {
                continue;
            }

            var item = SeasonCompiler.Copy(pair.Value);
            var bundle = _itemBundles.Resolve(item.Properties.Prefab!.Path!);
            if (bundle == null)
            {
                continue;
            }

            item.Properties.Prefab!.Path = bundle;
            if ((string?)item.Parent == "6a28212a0368f4438b0d0a45")
            {
                item.Parent = "5448ecbe4bdc2d60728b4568";
            }

            staged[id] = json.Deserialize<TemplateItem>(JsonConvert.SerializeObject(item))!;
        }

        var remaining = definition
            .Items.Where(i => !templates.Items.ContainsKey(new MongoId(i.Id)) || _owners.GetValueOrDefault(i.Id) == definition.Id)
            .ToList();
        while (remaining.Count > 0)
        {
            var ready = remaining
                .Where(i =>
                    staged.ContainsKey(new MongoId(i.CloneFrom))
                    || (!remaining.Any(r => r.Id == i.CloneFrom) && templates.Items.ContainsKey(new MongoId(i.CloneFrom)))
                )
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
                var node = cloner.Clone(original)!;
                string NewIdentity(string old)
                {
                    return old == original.Id.ToString()
                        ? item.Id
                        : SeasonRepository.Hash(System.Text.Encoding.UTF8.GetBytes(item.Id + ":" + old)).Substring(0, 24);
                }

                var slots = (node.Properties?.Slots ?? [])
                    .Concat(node.Properties?.Chambers ?? [])
                    .Concat(node.Properties?.Cartridges ?? [])
                    .ToList();
                var ids = slots
                    .Where(s => s.Id.HasValue)
                    .Select(s => s.Id!.Value)
                    .Concat((node.Properties?.Grids ?? []).Select(g => new MongoId(g.Id)))
                    .Append(original.Id)
                    .Distinct()
                    .ToDictionary(id => id, id => new MongoId(NewIdentity(id.ToString())));
                foreach (var slot in slots)
                {
                    if (slot.Id is { } slotId)
                    {
                        slot.Id = ids[slotId];
                    }

                    if (slot.Parent is { } parent && ids.TryGetValue(parent, out var replacement))
                    {
                        slot.Parent = replacement;
                    }
                }
                foreach (var grid in node.Properties?.Grids ?? [])
                {
                    grid.Id = ids[new MongoId(grid.Id)].ToString();
                    if (ids.TryGetValue(new MongoId(grid.Parent), out var replacement))
                    {
                        grid.Parent = replacement.ToString();
                    }
                }
                node.Id = new MongoId(item.Id);
                node.Name = item.Name;
                node.Properties!.Width = item.Width;
                node.Properties.Height = item.Height;
                node.Properties.StackMaxSize = item.StackMax;
                staged[new MongoId(item.Id)] = node;
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

        foreach (var craft in definition.Crafts)
        {
            if (
                hideout.Production.Recipes.Any(r => r.Id.ToString() == craft.Id)
                && (!_craftOwners.TryGetValue(craft.Id, out var owner) || owner != definition.Id)
            )
                throw new InvalidDataException("Campaign recipe collides with installed content: " + craft.Id);
            var native = json.Deserialize<HideoutProduction>(JsonConvert.SerializeObject(craft))!;
            hideout.Production.Recipes.RemoveAll(r => r.Id == native.Id);
            hideout.Production.Recipes.Add(native);
            _craftOwners[craft.Id] = definition.Id;
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

    private readonly Dictionary<string, string> _craftOwners = new();
}
