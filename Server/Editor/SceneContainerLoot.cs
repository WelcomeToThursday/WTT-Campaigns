using System.Reflection;
using Newtonsoft.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Generators.Loot;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Cloners;
using WTT.Campaigns.Shared.Native;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Server.Editor;

[Injectable(InjectionType.Singleton)]
public sealed class SceneContainerLoot(LocationTable locations, LocationLootGenerator generator, ICloner cloner, JsonUtil json)
{
    // Invoke the installed native implementation rather than duplicating its weighting, fitting or preset rules.
    internal static readonly MethodInfo Generate =
        typeof(LocationLootGenerator).GetMethod("AddLootToContainer", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("SPT native container loot generation is unavailable.");
    private readonly object _gate = new();

    public List<string> Templates(string map)
    {
        var sources = Sources(map).ToList();
        var distributions = sources.SelectMany(p => p.Value.StaticLoot.Value.Keys).Select(k => k.ToString()).ToHashSet();
        return sources
            .SelectMany(p => p.Value.StaticContainers.Value.StaticContainers ?? [])
            .Select(c => c.Template?.Items?.FirstOrDefault()?.Template.ToString() ?? "")
            .Where(distributions.Contains)
            .Distinct()
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
    }

    private IEnumerable<KeyValuePair<string, Location>> Sources(string map) =>
        locations
            .GetDictionary()
            .Where(p => p.Value != null && p.Value.StaticContainers != null && p.Value.StaticLoot != null && p.Value.StaticAmmo != null)
            .OrderBy(p => p.Key == map ? 0 : 1)
            .ThenBy(p => p.Key, StringComparer.Ordinal);

    public Dictionary<string, List<NativeItem>> Create(MapLayout layout)
    {
        var result = new Dictionary<string, List<NativeItem>>();
        lock (_gate)
            foreach (var placement in layout.Objects.Where(o => SceneAssetRules.IsContainer(o)))
            {
                if (placement.Target.IsAsset && !SceneAssetRules.Valid(placement.Target))
                    throw new InvalidOperationException("Invalid container reference.");
                var template = placement.Target.Template;
                var source = Sources(layout.Location)
                    .FirstOrDefault(p =>
                        p.Value.StaticLoot.Value.Keys.Any(k => k.ToString() == template)
                        && p.Value.StaticContainers.Value.StaticContainers.Any(c =>
                            c.Template.Items.FirstOrDefault()?.Template.ToString() == template
                        )
                    );
                if (source.Value == null)
                    throw new InvalidOperationException("No native loot mapping for container " + placement.Name);
                var native = cloner.Clone(
                    source.Value.StaticContainers.Value.StaticContainers.First(c =>
                        c.Template.Items.First().Template.ToString() == template
                    )
                )!;
                native.Template.Id = placement.Id;
                native.Template.Items = native.Template.Items.Take(1).ToList();
                var generated = (StaticContainerData)
                    Generate.Invoke(
                        generator,
                        [native, Array.Empty<StaticForced>(), source.Value.StaticLoot.Value, source.Value.StaticAmmo, source.Key]
                    )!;
                var tree = JsonConvert.DeserializeObject<List<NativeItem>>(json.Serialize(generated.Template.Items)!)!;
                var validation = new WTT.Campaigns.Shared.Seasons.SeasonValidationResult();
                WTT.Campaigns.Shared.Seasons.SeasonValidator.ItemTree(tree, "Generated container", validation);
                if (!validation.CanPublish)
                    throw new InvalidOperationException(validation.Issues[0].Message);
                result.Add(placement.Id, tree);
            }
        return result;
    }
}
