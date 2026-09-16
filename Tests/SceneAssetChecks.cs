using Mono.Cecil;
using Newtonsoft.Json;
using WTT.Campaigns.Client.Authoring.Scenes;
using WTT.Campaigns.Server.Seasons;
using WTT.Campaigns.Shared.Authoring;
using WTT.Campaigns.Shared.Missions;
using WTT.Campaigns.Shared.Native;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Tests;

internal static class SceneAssetChecks
{
    internal static void Run(string nativeAssembly, Action<bool, string> check)
    {
        var legacyTarget = Newtonsoft.Json.Linq.JObject.FromObject(new MapTarget());
        check(
            legacyTarget.Property("Bundle") == null && legacyTarget.Property("Asset") == null && legacyTarget.Property("IsAsset") == null,
            "New optional asset fields do not alter old scene-target serialization or package hashes"
        );
        var layout = MapEditorChecks.Example();
        var target = new MapTarget
        {
            Kind = "AssetProp",
            Bundle = "assets/props/crate.bundle",
            Asset = "assets/props/crate.prefab",
        };
        target.Fingerprint = SceneAssetRules.Identity(target.Bundle, target.Asset);
        var edit = new MapObjectEdit
        {
            Id = SeasonRepository.NewId(),
            Name = "Cross-map crate",
            Location = layout.Location,
            Scene = layout.Start!.Scene,
            Target = target,
            Operation = "Copy",
        };
        layout.Objects.Add(edit);
        check(
            MapLayoutRules.Errors(layout).Count == 0 && MapLayoutRules.Format([layout]) == 8,
            "Independent assets save in format 8 without source-map bindings"
        );
        var restored = JsonConvert.DeserializeObject<WTT.Campaigns.Shared.Spatial.MapLayout>(JsonConvert.SerializeObject(layout))!;
        check(
            restored.Objects.Last().Target.Asset == target.Asset && restored.Objects.Last().Target.Bundle == target.Bundle,
            "Layout round trip preserves unresolved asset references"
        );
        foreach (var path in new[] { "../crate", "/crate", "C:/crate", "assets/../crate", "assets\\crate", "assets//crate", "" })
            check(!SceneAssetRules.SafePath(path), "Reject malformed asset path " + path);
        check(
            SceneAssetRules.Identity(target.Bundle, "variant.prefab") != target.Fingerprint,
            "Distinct asset variants retain separate identities"
        );
        target.Bundle = "assets/other.bundle";
        check(MapLayoutRules.Errors(layout).Any(e => e.Contains("asset")), "Changed asset identity cannot reuse an old fingerprint");
        target.Bundle = "assets/props/crate.bundle";
        edit.Operation = "Move";
        check(MapLayoutRules.Errors(layout).Any(e => e.Contains("asset")), "Asset placement cannot masquerade as an original scene edit");
        edit.Operation = "Copy";
        target.Kind = "AssetContainer";
        target.Template = "578f8778245977358849a9b5";
        check(MapLayoutRules.Errors(layout).Count == 0, "Native container references support format 8 placement");
        edit.Scale.X = 2;
        check(
            MapLayoutRules.Errors(layout).Any(e => e.Contains("original size")),
            "Container scaling cannot break native grips and animation"
        );
        edit.Scale.X = 1;
        edit.Container = new ContainerSettings
        {
            Mode = "Fixed",
            SpawnChance = 35,
            Contents = [new ContainerContent { Template = "544fb45d4bdc2dee738b4568", Count = 3 }],
        };
        check(
            MapLayoutRules.Format([layout]) == 9 && MapLayoutRules.Errors(layout).Count == 0,
            "Configured containers require format 9 and accept fixed item quantities and spawn chance"
        );
        var configured = JsonConvert.DeserializeObject<MapObjectEdit>(JsonConvert.SerializeObject(edit))!;
        check(
            configured.Container?.SpawnChance == 35 && configured.Container.Contents.Single().Count == 3,
            "Container settings survive layout persistence"
        );
        foreach (var chance in new[] { -1, 101 })
        {
            edit.Container.SpawnChance = chance;
            check(MapLayoutRules.Errors(layout).Any(e => e.Contains("spawn chance")), "Reject invalid container spawn chance");
        }
        edit.Container.SpawnChance = 0;
        check(MapLayoutRules.Errors(layout).Count == 0, "A zero-percent container remains a valid authored placement");
        edit.Container.Locked = true;
        check(MapLayoutRules.Errors(layout).Any(e => e.Contains("Choose a key")), "Locked containers require an unlock key");
        edit.Container.KeyTemplate = "5938144586f77473c2087145";
        check(MapLayoutRules.Errors(layout).Count == 0, "Locked containers preserve an authored key identity");
        edit.Container.Contents[0].Count = 0;
        check(MapLayoutRules.Errors(layout).Any(e => e.Contains("fixed container")), "Zero fixed quantities are rejected");
        edit.Container = null;

        var stamp = SceneAssetRules.CacheFingerprint(new[] { "crate:100:10", "materials:200:20" });
        check(
            stamp == SceneAssetRules.CacheFingerprint(new[] { "materials:200:20", "crate:100:10" }),
            "Cache identity is independent of graph enumeration order"
        );
        check(
            stamp != SceneAssetRules.CacheFingerprint(new[] { "crate:100:10", "materials:200:21" }),
            "Changed dependency metadata invalidates the cached catalog classification"
        );
        check(
            stamp != SceneAssetRules.CacheFingerprint(new[] { "crate:100:10", "materials:missing" }),
            "Missing dependencies invalidate cached availability"
        );

        var cache = new SceneContainerRunCache(2);
        var run = SeasonRepository.NewId();
        var other = SeasonRepository.NewId();
        var calls = 0;
        SceneContainerResponse Generate()
        {
            calls++;
            return new()
            {
                Contents = new() { [edit.Id] = [new NativeItem { Id = SeasonRepository.NewId(), Template = target.Template }] },
            };
        }
        var first = cache.Get(run, layout.Id, Generate);
        var itemId = first.Contents[edit.Id][0].Id;
        first.Contents.Clear();
        var replay = cache.Get(run, layout.Id, Generate);
        check(calls == 1 && replay.Contents[edit.Id][0].Id == itemId, "Retries cannot reroll loot or mutate the cached response");
        var denied = false;
        try
        {
            cache.Get(run, other, Generate);
        }
        catch (InvalidOperationException)
        {
            denied = true;
        }
        check(denied && calls == 1, "A run cannot be reused for another layout");
        cache.Get(other, layout.Id, Generate);
        denied = false;
        try
        {
            cache.Get(SeasonRepository.NewId(), layout.Id, Generate);
        }
        catch (InvalidOperationException)
        {
            denied = true;
        }
        check(
            denied && calls == 2 && cache.Get(run, layout.Id, Generate).Contents[edit.Id][0].Id == itemId,
            "Capacity limits preserve old receipts instead of enabling rerolls"
        );
        var anotherSession = new SceneContainerRunCache();
        check(anotherSession.Get(run, layout.Id, Generate).Contents[edit.Id][0].Id != itemId, "Editor sessions own separate loot receipts");
        var persisted = new MissionRun { RunId = run, ContainerLoot = replay.Contents };
        check(
            JsonConvert.DeserializeObject<MissionRun>(JsonConvert.SerializeObject(persisted))!.ContainerLoot[edit.Id][0].Id == itemId,
            "Mission run persistence retains generated container identities across server reloads"
        );

        var late = new TaskCompletionSource<string>();
        var released = new List<string>();
        var lease = new SceneModelLease<string>(released.Add);
        var pending = lease.Load(_ => late.Task);
        lease.Dispose();
        late.SetResult("asset dependency token");
        pending.GetAwaiter().GetResult();
        check(
            released.SequenceEqual(new[] { "asset dependency token" }) && lease.Model == null,
            "A late asset completion releases dependencies after the editor closes"
        );

        var generation = typeof(SPTarkov.Server.Core.Generators.Loot.LocationLootGenerator).GetMethod(
            "AddLootToContainer",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
        );
        check(
            generation?.ReturnType == typeof(SPTarkov.Server.Core.Models.Eft.Common.StaticContainerData)
                && generation.GetParameters().Length == 5,
            "SPT native generation retains the expected container/distribution interface"
        );
        using var assembly = AssemblyDefinition.ReadAssembly(nativeAssembly);
        var types = assembly.MainModule.Types.ToDictionary(t => t.FullName);
        var loot = types["EFT.Interactive.LootItem"];
        check(
            loot.Methods.Any(m => m.Name == "CreateLootContainer" && m.IsStatic && m.Parameters.Count == 5),
            "Installed EFT exposes native container initialization"
        );
        check(
            types["EFT.GameWorld"].Methods.Any(m => m.Name == "RegisterLoot" && m.HasGenericParameters),
            "Installed EFT registers container item owners through native loot registration"
        );
        check(
            types["Diz.Resources.EasyBundle"].Fields.Any(f => f.Name == "_bundle")
                && types["Diz.DependencyManager.DependencyGraph`1"]
                    .NestedTypes.Any(t => t.Name == "TokenBase" && t.Methods.Any(m => m.Name == "Release")),
            "Installed native asset ownership exposes retained bundles and release tokens"
        );
    }
}
