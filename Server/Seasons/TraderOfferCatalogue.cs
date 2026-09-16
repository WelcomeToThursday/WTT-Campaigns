using System.Text;
using Newtonsoft.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Locales;
using SPTarkov.Server.Core.Utils;
using WTT.Campaigns.Shared.Authoring;
using WTT.Campaigns.Shared.Native;
using WTT.Campaigns.Shared.Seasons;

namespace WTT.Campaigns.Server.Seasons;

[Injectable(InjectionType.Singleton)]
public sealed class TraderOfferCatalogue(
    TemplateTable templates,
    TradersTable traders,
    GlobalTable globals,
    LocaleService locales,
    JsonUtil json,
    ItemHelper itemHelper
)
{
    // Keep native price, quest-item, blacklist and inventory-category rules.
    // Hideout area containers are not included in SPT's default invalid bases.
    public bool IsInventoryItem(string id) =>
        SeasonValidator.IsId(id)
        && templates.Items.TryGetValue(new MongoId(id), out var item)
        && itemHelper.IsValidItem(item)
        && !itemHelper.IsOfBaseclass(item.Id, BaseClasses.HIDEOUT_AREA_CONTAINER);

    // Scene placement has no price or trader blacklist requirement. Infrastructure and quest objects stay excluded.
    public bool IsSceneItem(string id) =>
        SeasonValidator.IsId(id)
        && templates.Items.TryGetValue(new MongoId(id), out var item)
        && string.Equals(item.Type, "Item", StringComparison.OrdinalIgnoreCase)
        && item.Properties?.QuestItem != true
        && !new[]
        {
            BaseClasses.LOOT_CONTAINER,
            BaseClasses.MOB_CONTAINER,
            BaseClasses.STASH,
            BaseClasses.SORTING_TABLE,
            BaseClasses.INVENTORY,
            BaseClasses.STATIONARY_CONTAINER,
            BaseClasses.POCKETS,
            BaseClasses.HIDEOUT_AREA_CONTAINER,
        }.Any(b => itemHelper.IsOfBaseclass(item.Id, b));

    private sealed class Shape
    {
        public int Width { get; set; } = 1;
        public int Height { get; set; } = 1;
        public int StackMaxSize { get; set; } = 1;
        public List<string> ConflictingItems { get; set; } = [];
        public List<Container> Slots { get; set; } = [];
        public List<Container> Chambers { get; set; } = [];
        public List<Container> Cartridges { get; set; } = [];
        public List<Container> Grids { get; set; } = [];
    }

    private sealed class Container
    {
        [JsonProperty("_name")]
        public string Name { get; set; } = "";

        [JsonProperty("_required")]
        public bool Required { get; set; }

        [JsonProperty("_max_count")]
        public int Max { get; set; } = 1;

        [JsonProperty("_props")]
        public ContainerProps Props { get; set; } = new();
    }

    private sealed class ContainerProps
    {
        public int CellsH { get; set; }
        public int CellsV { get; set; }
        public List<Filter> Filters { get; set; } = [];
    }

    private sealed class Filter
    {
        [JsonProperty("Filter")]
        public List<string> Allowed { get; set; } = [];
        public List<string> ExcludedFilter { get; set; } = [];
    }

    public OfferItemInfo? Item(string id)
    {
        if (!SeasonValidator.IsId(id) || !templates.Items.TryGetValue(new MongoId(id), out var template))
            return null;
        var raw = json.Serialize(template.Properties) ?? "{}";
        var shape = JsonConvert.DeserializeObject<Shape>(raw) ?? new();
        var locale = locales.GetLocaleDb("en");
        var info = new OfferItemInfo
        {
            Id = id,
            Name = locale.GetValueOrDefault(id + " Name", template.Name ?? id),
            Parent = template.Parent.ToString(),
            Width = shape.Width,
            Height = shape.Height,
            StackMax = Math.Max(1, shape.StackMaxSize),
            Hash = SeasonRepository.Hash(Encoding.UTF8.GetBytes(template.Type + ":" + template.Parent + ":" + raw)),
            Conflicts = shape.ConflictingItems ?? [],
        };
        void Add(IEnumerable<Container>? records, string kind)
        {
            foreach (var c in records ?? [])
                info.Containers.Add(
                    new()
                    {
                        Id = c.Name,
                        Kind = kind,
                        Required = c.Required,
                        Width = c.Props.CellsH,
                        Height = c.Props.CellsV,
                        Capacity = c.Max,
                        Allowed = c.Props.Filters.SelectMany(f => f.Allowed ?? []).Distinct().ToList(),
                        AllowedGroups = c.Props.Filters.Select(f => f.Allowed ?? []).ToList(),
                        Excluded = c.Props.Filters.SelectMany(f => f.ExcludedFilter ?? []).Distinct().ToList(),
                    }
                );
        }
        Add(shape.Slots, "Slot");
        Add(shape.Chambers, "Slot");
        Add(shape.Cartridges, "Ammo");
        Add(shape.Grids, "Grid");
        return info;
    }

    public bool Compatible(OfferContainer container, string template)
    {
        var parents = new HashSet<string>();
        while (SeasonValidator.IsId(template) && parents.Add(template) && templates.Items.TryGetValue(new MongoId(template), out var t))
            template = t.Parent.ToString();
        return !container.Excluded.Any(parents.Contains)
            && (
                container.AllowedGroups.Count > 0
                    ? container.AllowedGroups.All(g => g.Any(parents.Contains))
                    : container.Allowed.Count == 0 || container.Allowed.Any(parents.Contains)
            );
    }

    public List<(string Id, string Name)> Presets(string search)
    {
        return globals
            .ItemPresets.Values.Select(p => (p.Id.ToString(), p.Name ?? p.Id.ToString()))
            .Where(p => p.Item2.Contains(search, StringComparison.OrdinalIgnoreCase))
            .Take(100)
            .ToList();
    }

    public List<OfferItemInfo> Search(string search, OfferContainer? container = null)
    {
        var locale = locales.GetLocaleDb("en");
        return templates
            .Items.Values.Where(t => IsInventoryItem(t.Id.ToString()))
            .Select(t => (Id: t.Id.ToString(), Name: locale.GetValueOrDefault(t.Id + " Name", t.Name ?? t.Id.ToString())))
            .Where(t =>
                (
                    search.Length == 0
                    || t.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || t.Id.Contains(search, StringComparison.OrdinalIgnoreCase)
                ) && (container == null || Compatible(container, t.Id))
            )
            .OrderBy(t => t.Name)
            .Take(24)
            .Select(t => Item(t.Id)!)
            .ToList();
    }

    public List<NativeItem> Preset(string id) =>
        JsonConvert.DeserializeObject<List<NativeItem>>(json.Serialize(globals.ItemPresets[new MongoId(id)].Items)!)!;

    public SceneCatalogResponse SceneCatalog(SceneCatalogRequest request)
    {
        if (
            (request.TemplateIds != null && (request.TemplateIds.Count > 10000 || request.TemplateIds.Any(id => !SeasonValidator.IsId(id))))
            || request.Page < 0
            || request.Page > 10000
            || request.Id == null
            || request.Search == null
            || request.Search.Length > 120
            || request.Category is not ("Items" or "Presets" or "Keys")
        )
            throw new InvalidOperationException("Invalid catalog query.");
        var locale = locales.GetLocaleDb("en");
        var entries =
            request.Category == "Presets"
                ? globals.ItemPresets.Values.Select(p => new SceneCatalogEntry { Id = p.Id.ToString(), Name = p.Name ?? p.Id.ToString() })
                : templates
                    .Items.Values.Where(t =>
                        IsSceneItem(t.Id.ToString()) && (request.Category != "Keys" || itemHelper.IsOfBaseclass(t.Id, BaseClasses.KEY))
                    )
                    .Select(t => new SceneCatalogEntry
                    {
                        Id = t.Id.ToString(),
                        Name = locale.GetValueOrDefault(t.Id + " Name", t.Name ?? t.Id.ToString()),
                    });
        entries = entries.Where(e =>
            e.Name.Contains(request.Search, StringComparison.OrdinalIgnoreCase)
            || e.Id.Contains(request.Search, StringComparison.OrdinalIgnoreCase)
        );
        if (request.TemplateIds != null)
        {
            var ids = request.TemplateIds.ToHashSet();
            entries = entries.Where(e =>
                request.Category != "Presets"
                    ? ids.Contains(e.Id)
                    : globals.ItemPresets[new MongoId(e.Id)].Items.Any(i => i.ParentId == null && ids.Contains(i.Template.ToString()))
            );
        }
        if (request.Id.Length > 0)
            entries = entries.Where(e => e.Id == request.Id);
        var sorted = entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Id, StringComparer.Ordinal).ToList();
        var response = new SceneCatalogResponse { Total = sorted.Count, Entries = sorted.Skip(request.Page * 10).Take(10).ToList() };
        foreach (var entry in response.Entries)
        {
            try
            {
                entry.Items =
                    request.Category == "Presets"
                        ? Web.Authoring.TraderOfferAuthoring.PreviewAssembly(Preset(entry.Id))
                        : SceneItem(entry.Id);
                if (entry.Items.Any(i => !IsSceneItem(i.Template)))
                    throw new InvalidOperationException("This preset contains unavailable inventory items.");
                var validation = new SeasonValidationResult();
                SeasonValidator.ItemTree(entry.Items, "Catalog item", validation);
                if (!validation.CanPublish)
                    throw new InvalidOperationException(validation.Issues[0].Message);
            }
            catch (Exception e)
            {
                entry.Items = new();
                entry.Error = e.Message;
            }
        }
        return response;
    }

    internal List<NativeItem> SceneItem(string templateId)
    {
        if (string.IsNullOrWhiteSpace(templates.Items[new MongoId(templateId)].Properties?.Prefab?.Path))
            throw new InvalidOperationException("This item has no registered world model.");
        var root = new NativeItem { Id = SeasonRepository.NewId(), Template = templateId };
        if (!itemHelper.IsOfBaseclass(new MongoId(templateId), BaseClasses.AMMO_BOX))
            return [root];
        var template = templates.Items[new MongoId(templateId)];
        var slot = template.Properties?.StackSlots?.FirstOrDefault();
        var ammoId = slot?.Properties?.Filters?.FirstOrDefault()?.Filter?.FirstOrDefault();
        if (
            slot?.MaxCount is not > 0
            || ammoId == null
            || !templates.Items.TryGetValue(ammoId.Value, out var ammo)
            || ammo.Properties?.StackMaxSize is not > 0
            || !IsSceneItem(ammoId.Value.ToString())
        )
            throw new InvalidOperationException("This ammunition box has no valid native contents.");
        var items = new List<SPTarkov.Server.Core.Models.Eft.Common.Tables.Item>
        {
            new() { Id = new MongoId(root.Id), Template = new MongoId(templateId) },
        };
        itemHelper.AddCartridgesToAmmoBox(items, template);
        return JsonConvert.DeserializeObject<List<NativeItem>>(json.Serialize(items)!)!;
    }

    public List<List<NativeBarter>> OfferCosts(string composite)
    {
        var parts = composite.Split('/');
        return JsonConvert.DeserializeObject<List<List<NativeBarter>>>(
            json.Serialize(traders[new MongoId(parts[0])].Assort.BarterScheme[new MongoId(parts[1])])!
        )!;
    }

    public bool HasTrader(string id) =>
        SeasonValidator.IsId(id)
        && id != TraderOfferRules.Fence
        && traders.TryGetValue(new MongoId(id), out var trader)
        && trader.Assort != null;

    public bool HasOffer(string trader, string offer) =>
        HasTrader(trader)
        && SeasonValidator.IsId(offer)
        && traders[new MongoId(trader)].Assort.BarterScheme.ContainsKey(new MongoId(offer));

    public List<CampaignTraderOffer> InstalledOffers(string traderId)
    {
        if (!HasTrader(traderId))
            return [];
        var assort = traders[new MongoId(traderId)].Assort;
        var items = JsonConvert.DeserializeObject<List<NativeItem>>(json.Serialize(assort.Items)!)!;
        var children = items.Where(i => i.ParentId != null).ToLookup(i => i.ParentId!);
        var result = new List<CampaignTraderOffer>();
        foreach (var root in items.Where(i => assort.BarterScheme.ContainsKey(new MongoId(i.Id))))
        {
            var tree = new List<NativeItem>();
            var pending = new Stack<NativeItem>();
            var seen = new HashSet<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                var item = pending.Pop();
                if (!seen.Add(item.Id))
                    continue;
                tree.Add(item);
                foreach (var child in children[item.Id])
                    pending.Push(child);
            }
            result.Add(
                new()
                {
                    Id = root.Id,
                    TraderId = traderId,
                    Name = Item(root.Template)?.Name ?? root.Template,
                    Items = tree,
                    Barter = OfferCosts(traderId + "/" + root.Id),
                    Loyalty = assort.LoyalLevelItems.GetValueOrDefault(new MongoId(root.Id), 1),
                    UnlimitedStock = root.Upd?.UnlimitedCount == true,
                    Stock = (int)Math.Clamp(root.Upd?.StackObjectsCount ?? 100, 1, 10000000),
                    PurchaseLimit = root.Upd?.BuyRestrictionMax ?? 0,
                }
            );
        }
        return result.OrderBy(o => o.Name).ToList();
    }

    public string ContentHash(IEnumerable<NativeItem> items)
    {
        var ids = new HashSet<string>();
        foreach (var item in items)
        {
            var id = item.Template;
            while (SeasonValidator.IsId(id) && ids.Add(id) && templates.Items.TryGetValue(new MongoId(id), out var template))
                id = template.Parent.ToString();
        }
        return SeasonRepository.Hash(
            Encoding.UTF8.GetBytes(string.Join("|", ids.Order().Select(id => id + ":" + (Item(id)?.Hash ?? "missing"))))
        );
    }

    public void Validate(CampaignTraderOffer offer, SeasonValidationResult result, IReadOnlyList<PreviewItemSize>? sizes = null)
    {
        var path = "Trader offers/" + offer.Id;
        if (
            !SeasonValidator.IsId(offer.TraderId)
            || !traders.TryGetValue(new MongoId(offer.TraderId), out var trader)
            || trader.Assort == null
        )
            result.Add(path, "Trader is not installed.", "dependency");
        var tree = new SeasonValidationResult();
        SeasonValidator.ItemTree(offer.Items, path, tree);
        if (!tree.CanPublish)
        {
            result.Issues.AddRange(tree.Issues);
            return;
        }
        var info = offer.Items.ToDictionary(i => i.Id, i => Item(i.Template));
        foreach (var item in offer.Items)
        {
            if (info[item.Id] == null)
            {
                result.Add(path, "Item is not installed: " + item.Template, "dependency");
                continue;
            }
            if (!IsInventoryItem(item.Template))
            {
                result.Add(path, "This template is not a purchasable inventory item: " + item.Template);
                continue;
            }
            if (item.Id == offer.Id)
                continue;
            var container =
                item.ParentId == null ? null : info.GetValueOrDefault(item.ParentId)?.Containers.FirstOrDefault(c => c.Id == item.SlotId);
            if (container == null)
            {
                result.Add(path, "Unknown parent slot: " + item.SlotId);
                continue;
            }
            if (!Compatible(container, item.Template))
                result.Add(path, "Item does not fit " + container.Id + ": " + info[item.Id]!.Name);
            if ((item.Upd?.StackObjectsCount ?? 1) > info[item.Id]!.StackMax)
                result.Add(path, "Item stack exceeds its template capacity.");
            var siblings = offer.Items.Where(i => i.ParentId == item.ParentId && i.SlotId == item.SlotId).ToArray();
            if (container.Kind == "Slot" && siblings.Length > 1)
                result.Add(path, "A slot contains more than one item.");
            if (
                container.Kind == "Ammo"
                && (
                    siblings.Sum(i => i.Upd?.StackObjectsCount ?? 1) > container.Capacity
                    || siblings.Any(i => i.Location?.Slot == null)
                    || !siblings.Select(i => i.Location!.Slot ?? -1).Order().SequenceEqual(Enumerable.Range(0, siblings.Length))
                )
            )
                result.Add(path, "Invalid ammunition capacity or positions.");
            if (container.Kind == "Grid")
            {
                var cells = new HashSet<(int, int)>();
                foreach (var child in siblings)
                {
                    var p = child.Location?.Grid;
                    var descriptor = info[child.Id];
                    if (p == null || descriptor == null)
                    {
                        result.Add(path, "Grid item needs a position.");
                        continue;
                    }
                    var rotated = p.Rotation is "1" or "Vertical" || p.Rotated == true;
                    var index = offer.Items.IndexOf(child);
                    var size = sizes?.Count == offer.Items.Count ? sizes[index] : null;
                    // Folding and nested attachments can change native cell dimensions.
                    // Defer their geometry until the required client check supplies it.
                    if (size == null && (child.Upd?.Foldable != null || offer.Items.Any(i => i.ParentId == child.Id)))
                        continue;
                    int width = size?.Width ?? descriptor.Width,
                        height = size?.Height ?? descriptor.Height;
                    int w = rotated ? height : width,
                        h = rotated ? width : height;
                    if (p.X < 0 || p.Y < 0 || p.X + w > container.Width || p.Y + h > container.Height)
                        result.Add(path, "Grid item is outside its container.");
                    for (int x = p.X ?? 0; x < p.X + w; x++)
                    for (int y = p.Y ?? 0; y < p.Y + h; y++)
                        if (!cells.Add((x, y)))
                            result.Add(path, "Grid items overlap.");
                }
            }
        }
        foreach (var cost in offer.Barter.SelectMany(b => b))
            if (Item(cost.Template) == null)
                result.Add(path, "Missing barter item: " + cost.Template, "dependency");
            else if (!IsInventoryItem(cost.Template))
                result.Add(path, "This template cannot be used as a barter item: " + cost.Template);
    }
}
