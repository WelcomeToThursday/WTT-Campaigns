using Comfort.Common;
using Diz.Jobs;
using EFT;
using EFT.AssetsManager;
using EFT.CameraControl;
using EFT.Interactive;
using EFT.InventoryLogic;
using Newtonsoft.Json;
using SPT.Common.Http;
using UnityEngine;
using UnityEngine.SceneManagement;
using WTT.Campaigns.Client.Authoring.Scenes;
using WTT.Campaigns.Client.Spatial;
using WTT.Campaigns.Shared.Authoring;
using WTT.Campaigns.Shared.Native;
using WTT.Campaigns.Shared.Spatial;
using ZLinq;

namespace WTT.Campaigns.Client.Missions;

/// <summary>Creates ordinary native loot owners; collected items belong to the player's native inventory.</summary>
internal sealed class MissionLoot : IDisposable
{
    private readonly List<(LootItem Loot, string ItemId)> _owned = new();
    private GameWorld? _world;
    private bool _begun;
    private bool _disposed;
    private readonly List<(SceneAssetCatalog.Model Model, LootableContainer Container, SceneNavigation Navigation)> _containers = new();

    internal async Task ApplyAsync(
        MapLayout layout,
        string runId,
        CancellationToken token,
        Dictionary<string, List<NativeItem>>? containerLoot = null
    )
    {
        using var loading = UI.NativeLoadingStatus.Begin("Preparing placed items…");
        if (_begun || _disposed)
            throw new InvalidOperationException("Create a fresh loot owner for each mission attempt.");
        _begun = true;
        if (string.IsNullOrWhiteSpace(runId))
            throw new InvalidOperationException("Mission loot requires a run identity.");
        if (!Singleton<GameWorld>.Instantiated)
            throw new InvalidOperationException("The mission world is unavailable.");
        var world = _world = Singleton<GameWorld>.Instance;
        var factory = Singleton<ObjectsFactory>.Instance;
        var itemFactory = Singleton<ItemFactory>.Instance;
        try
        {
            if (layout.Objects.AsValueEnumerable().Any(p => SceneAssetRules.IsContainer(p)))
            {
                if (containerLoot == null && Authoring.EditorMode.Ready)
                {
                    var response = JsonConvert.DeserializeObject<SceneContainerResponse>(
                        await RequestHandler.PostJsonAsync(
                            "/wtt-campaigns/editor/containers",
                            JsonConvert.SerializeObject(
                                new SceneContainerRequest
                                {
                                    SessionId = Authoring.EditorMode.SessionId,
                                    LayoutId = layout.Id,
                                    RunId = runId,
                                }
                            )
                        )
                    );
                    RequireWorld(world, token);
                    if (response == null || response.Error != null)
                        throw new InvalidOperationException(response?.Error ?? "Container service did not respond.");
                    containerLoot = response.Contents;
                }
                foreach (var placement in layout.Objects)
                {
                    if (!SceneAssetRules.IsContainer(placement))
                        continue;
                    if (containerLoot == null || !containerLoot.TryGetValue(placement.Id, out var contents))
                        throw new InvalidOperationException("This run has no server-generated contents for " + placement.Name);
                    if (contents.Count == 0)
                        continue; // Persisted spawn-chance miss.
                    var model = await SceneAssetCatalog.Load(placement.Target, token);
                    LootableContainer? container = null;
                    SceneNavigation? navigation = null;
                    try
                    {
                        RequireWorld(world, token);
                        container = model.Object.GetComponentInChildren<LootableContainer>(true);
                        if (!container || container.Template != placement.Target.Template)
                            throw new InvalidOperationException("Container template changed.");
                        container.Id = "wtt-container-" + runId + "-" + placement.Id;
                        model.Object.transform.SetPositionAndRotation(
                            ZoneRuntime.Vector(placement.Position),
                            Quaternion.Euler(ZoneRuntime.Vector(placement.Rotation))
                        );
                        if (contents.Count == 0 || contents[0].Template != placement.Target.Template)
                            throw new InvalidOperationException(
                                "The container changed after this run was prepared; start a new rehearsal."
                            );
                        var contentResources = new List<ResourceKey>();
                        foreach (var record in contents)
                        {
                            if (!itemFactory.ItemTemplates.TryGetValue(record.Template, out var contentTemplate))
                                throw new InvalidOperationException("A generated container item is unavailable: " + record.Template);
                            if (contentTemplate.Prefab != null && !string.IsNullOrEmpty(contentTemplate.Prefab.path))
                                contentResources.Add(contentTemplate.Prefab);
                            if (contentTemplate.UsePrefab != null && !string.IsNullOrEmpty(contentTemplate.UsePrefab.path))
                                contentResources.Add(contentTemplate.UsePrefab);
                        }
                        await factory.LoadBundlesAndCreatePools(
                            ObjectsFactory.PoolsCategory.Raid,
                            ObjectsFactory.AssemblyType.Local,
                            contentResources.AsValueEnumerable().Distinct().ToArray(),
                            JobYieldPriority.Immediate,
                            null,
                            token
                        );
                        RequireWorld(world, token);
                        var item = SceneLootModel.Item(contents);
                        item.SpawnedInSession = true;
                        if (item is ContainerCollection containerItems)
                            foreach (var child in containerItems.GetAllItemsFromCollection())
                                child.SpawnedInSession = true;
                        LootItem.CreateLootContainer(container, item, placement.Name, world, container.Id);
                        if (placement.Container is { } settings)
                        {
                            container.KeyId = settings.KeyTemplate;
                            container.DoorState = settings.Locked ? EDoorState.Locked : EDoorState.Shut;
                        }
                        model.Object.SetActive(true);
                        navigation = new SceneNavigation(model.Object.transform);
                        _containers.Add((model, container, navigation));
                    }
                    catch
                    {
                        navigation?.Dispose();
                        if (container)
                        {
                            world.LootList.Remove(container);
                            if (container.ItemOwner != null)
                                world.ItemOwners.Remove(container.ItemOwner);
                        }
                        model.Dispose();
                        throw;
                    }
                }
            }
            if (_containers.Count > 0)
            {
                await MapSceneAdapter.WaitForNavigationAsync(token);
                RequireWorld(world, token);
            }
            foreach (var placement in layout.Loot)
            {
                RequireWorld(world, token);
                var scene = SceneManager.GetSceneByName(placement.Scene);
                if (!scene.IsValid() || !scene.isLoaded)
                    throw new InvalidOperationException("Placed loot scene is unavailable: " + placement.Name);
                var records = MissionLootRecords.CopyForRun(placement.Items);
                var resources = new List<ResourceKey>();
                foreach (var record in records)
                {
                    if (!itemFactory.ItemTemplates.TryGetValue(record.Template, out var template))
                        throw new InvalidOperationException("Placed loot template is unavailable: " + record.Template);
                    if (template.Prefab != null && !string.IsNullOrWhiteSpace(template.Prefab.path))
                        resources.Add(template.Prefab);
                    if (template.UsePrefab != null && !string.IsNullOrWhiteSpace(template.UsePrefab.path))
                        resources.Add(template.UsePrefab);
                }
                await factory.LoadBundlesAndCreatePools(
                    ObjectsFactory.PoolsCategory.Raid,
                    ObjectsFactory.AssemblyType.Local,
                    resources.AsValueEnumerable().Distinct().ToArray(),
                    JobYieldPriority.Immediate,
                    null,
                    token
                );
                RequireWorld(world, token);
                var item = SceneLootModel.Item(records);
                item.SpawnedInSession = true;
                if (item is ContainerCollection collection)
                    foreach (var child in collection.GetAllItemsFromCollection())
                        child.SpawnedInSession = true;

                // Match GameWorld.SpawnLootItem: the detached assembly needs a
                // native root address before LootItem.Init reads item.Parent.
                var owner = new ItemController(item, item.Id, item.ShortName);

                // This is the same prefab role and world constructor used by
                // GameWorld.SpawnLootItem, rather than the editor's inert model.
                GameObject? model = null;
                LootItem? loot = null;
                try
                {
                    model = factory.CreateLootPrefab(item, ECameraType.Default, null);
                    if (!model || !model.GetComponent<LootItem>())
                        throw new InvalidOperationException("The native loot prefab is unavailable: " + placement.Name);
                    // Keep the component available for rollback even if native
                    // initialization throws after registering it with the world.
                    loot = model.GetComponent<LootItem>();
                    model.transform.SetParent(null, false);
                    SceneManager.MoveGameObjectToScene(model, scene);
                    model.transform.SetPositionAndRotation(
                        ZoneRuntime.Vector(placement.Position),
                        Quaternion.Euler(ZoneRuntime.Vector(placement.Rotation))
                    );
                    model.SetActive(true);
                    // Null means ordinary public loot. An empty allowlist makes
                    // the native IsValidForProfile check reject every player.
                    loot = world.CreateStaticLoot(model, item, placement.Name, false, null, item.Id, Vector3.zero);
                    if (!loot || !world.LootList.Contains(loot) || loot.ItemOwner != owner || !world.ItemOwners.ContainsKey(owner))
                        throw new InvalidOperationException("The native world did not register placed loot: " + placement.Name);
                    _owned.Add((loot, item.Id));
                    model = null; // The native world now owns and pools it.
                }
                catch
                {
                    if (loot && world.LootList.Contains(loot))
                    {
                        world.DestroyLoot(loot);
                        model = null;
                    }
                    if (model)
                        AssetPoolObject.ReturnToPool(model, true);
                    throw;
                }
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private void RequireWorld(GameWorld world, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_disposed || !world || !Singleton<GameWorld>.Instantiated || Singleton<GameWorld>.Instance != world)
            throw new OperationCanceledException("The mission world ended while loot was being prepared.", token);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        // A taken item has already left LootList. Checking both membership and
        // item identity also avoids destroying an object reused by EFT's pool.
        if (_world && Singleton<GameWorld>.Instantiated && Singleton<GameWorld>.Instance == _world)
            foreach (var (loot, itemId) in _owned)
                if (loot && loot.Item?.Id == itemId && _world.LootList.Contains(loot))
                    _world.DestroyLoot(loot);
        _owned.Clear();
        foreach (var (model, container, navigation) in _containers)
        {
            navigation.Dispose();
            if (_world && container)
            {
                _world.LootList.Remove(container);
                if (container.ItemOwner != null)
                    _world.ItemOwners.Remove(container.ItemOwner);
            }
            model.Dispose();
        }
        _containers.Clear();
        _world = null;
    }
}
