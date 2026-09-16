using System.Reflection;
using Newtonsoft.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Generators.Loot;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Spt.Inventory;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Cloners;
using WTT.Campaigns.Shared.Native;
using WTT.Campaigns.Shared.Spatial;
using WTT.Campaigns.Server.Seasons;

namespace WTT.Campaigns.Server.Editor;

[Injectable(InjectionType.Singleton)]
public sealed class SceneContainerLoot(LocationTable locations, LocationLootGenerator generator, ICloner cloner, JsonUtil json,
    ItemHelper itemHelper, RandomUtil random, TraderOfferCatalogue catalogue)
{
    internal static readonly MethodInfo Generate = typeof(LocationLootGenerator).GetMethod("AddLootToContainer", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("SPT native container loot generation is unavailable.");
    private static readonly MethodInfo CreateItem = typeof(LocationLootGenerator).GetMethod("CreateStaticLootItem", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("SPT native item creation is unavailable.");
    private readonly object _gate = new();

    public List<string> Templates(string map) => Sources(map).SelectMany(p => p.Value.StaticLoot.Value.Keys)
        .Select(k => k.ToString()).Distinct().OrderBy(id => id, StringComparer.Ordinal).ToList();

    private IEnumerable<KeyValuePair<string, Location>> Sources(string map) => locations.GetDictionary()
        .Where(p => p.Value != null && p.Value.StaticContainers != null && p.Value.StaticLoot != null && p.Value.StaticAmmo != null)
        .OrderBy(p => p.Key == map ? 0 : 1).ThenBy(p => p.Key, StringComparer.Ordinal);

    public Dictionary<string, List<NativeItem>> Create(MapLayout layout)
    {
        var errors = MapLayoutRules.Errors(layout);
        if (errors.Count > 0) throw new InvalidOperationException(errors[0]);
        var result = new Dictionary<string, List<NativeItem>>();
        lock (_gate)
            foreach (var placement in layout.Objects.Where(SceneAssetRules.IsContainer))
            {
                var settings = placement.Container ?? new ContainerSettings();
                if (settings.Locked && !itemHelper.IsOfBaseclass(new MongoId(settings.KeyTemplate), SPTarkov.Server.Core.Models.Enums.BaseClasses.KEY))
                    throw new InvalidOperationException("The selected container key is not a native key.");
                // An explicit empty receipt means this container did not spawn. It is
                // cached/persisted with the run so retries cannot reroll its chance.
                if (settings.SpawnChance == 0 || (settings.SpawnChance < 100 && !random.GetChance100(settings.SpawnChance)))
                {
                    result.Add(placement.Id, []);
                    continue;
                }
                List<NativeItem> tree;
                if (settings.Mode == "Native") tree = RandomContents(layout.Location, placement, settings);
                else
                {
                    tree = [new NativeItem { Id = SeasonRepository.NewId(), Template = placement.Target.Template }];
                    if (settings.Mode == "Fixed") FillFixed(layout.Location, tree, settings.Contents);
                }
                var validation = new Shared.Seasons.SeasonValidationResult();
                Shared.Seasons.SeasonValidator.ItemTree(tree, "Generated container", validation);
                if (!validation.CanPublish) throw new InvalidOperationException(validation.Issues[0].Message);
                result.Add(placement.Id, tree);
            }
        return result;
    }

    private List<NativeItem> RandomContents(string map, MapObjectEdit placement, ContainerSettings settings)
    {
        var template = placement.Target.Template;
        var pool = settings.LootPool.Length > 0 ? settings.LootPool : template;
        var source = Sources(map).FirstOrDefault(p => p.Value.StaticLoot.Value.ContainsKey(new MongoId(pool)));
        var shell = Sources(map).SelectMany(p => p.Value.StaticContainers.Value.StaticContainers)
            .FirstOrDefault(c => c.Template.Items.FirstOrDefault()?.Template.ToString() == template);
        if (source.Value == null || shell == null)
            throw new InvalidOperationException("No native loot pool for " + placement.Name + ". Choose fixed contents or empty loot.");
        var native = cloner.Clone(shell)!;
        native.Template.Id = placement.Id;
        native.Template.Items = native.Template.Items.Take(1).ToList();
        // Preserve the chosen container's capacity while using the selected pool's
        // weights. Never mutate SPT's shared location tables.
        var distributions = new Dictionary<MongoId, StaticLootDetails>(source.Value.StaticLoot.Value)
        { [new MongoId(template)] = source.Value.StaticLoot.Value[new MongoId(pool)] };
        var generated = (StaticContainerData)Generate.Invoke(generator,
            [native, Array.Empty<StaticForced>(), distributions, source.Value.StaticAmmo, source.Key])!;
        return JsonConvert.DeserializeObject<List<NativeItem>>(json.Serialize(generated.Template.Items)!)!;
    }

    private void FillFixed(string map, List<NativeItem> tree, List<ContainerContent> contents)
    {
        var mapping = itemHelper.GetContainerMapping(new MongoId(tree[0].Template));
        var ammo = Sources(map).First().Value.StaticAmmo;
        foreach (var content in contents)
        {
            if (!catalogue.IsSceneItem(content.Template)) throw new InvalidOperationException("Invalid fixed loot item: " + content.Template);
            var maxStack = Math.Max(1, itemHelper.GetItem(new MongoId(content.Template)).Value?.Properties?.StackMaxSize ?? 1);
            for (var remaining = content.Count; remaining > 0;)
            {
                var item = (ContainerItem?)CreateItem.Invoke(generator, [new MongoId(content.Template), ammo, tree[0].Id])
                    ?? throw new InvalidOperationException("Unable to create fixed loot item.");
                var slot = mapping.FindSlotForItem(item.Width, item.Height);
                if (slot.Success != true) throw new InvalidOperationException("Fixed contents do not fit in this container. Reduce the quantities.");
                mapping.TryFillContainerMapWithItem(slot.X!.Value, slot.Y!.Value, item.Width, item.Height, slot.Rotation ?? false, out _);
                var records = JsonConvert.DeserializeObject<List<NativeItem>>(json.Serialize(item.Items)!)!;
                var count = Math.Min(remaining, maxStack);
                records[0].Upd ??= new NativeItemUpdate();
                records[0].Upd.StackObjectsCount = count;
                records[0].ParentId = tree[0].Id;
                records[0].SlotId = "main";
                records[0].Location = new NativeItemLocation(new NativeGridLocation { X = slot.X, Y = slot.Y, Rotation = slot.Rotation == true ? "Vertical" : "Horizontal" });
                tree.AddRange(records);
                remaining -= count;
            }
        }
    }
}
