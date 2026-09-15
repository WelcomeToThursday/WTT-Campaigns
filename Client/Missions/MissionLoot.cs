using Comfort.Common;
using Diz.Jobs;
using EFT;
using EFT.AssetsManager;
using EFT.CameraControl;
using EFT.Interactive;
using EFT.InventoryLogic;
using UnityEngine;
using UnityEngine.SceneManagement;
using WTT.Campaigns.Client.Authoring;
using WTT.Campaigns.Client.Spatial;
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

    internal async Task ApplyAsync(MapLayout layout, string runId, CancellationToken token)
    {
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
                    if (template.Prefab != null)
                        resources.Add(template.Prefab);
                    if (template.UsePrefab != null)
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
                    model.transform.SetPositionAndRotation(ZoneRuntime.Vector(placement.Position), Quaternion.Euler(ZoneRuntime.Vector(placement.Rotation)));
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
        _world = null;
    }
}
