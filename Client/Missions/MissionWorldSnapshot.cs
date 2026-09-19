using Comfort.Common;
using Cysharp.Threading.Tasks;
using EFT;
using EFT.Interactive;
using HarmonyLib;
using JsonType;
using UnityEngine;
using WTT.Campaigns.Client.Authoring.Scenes;
using ZLinq;

namespace WTT.Campaigns.Client.Missions;

/// <summary>World owners and player inventory must be cleared together before either is rebuilt.</summary>
internal sealed class MissionWorldSnapshot
{
    private static bool _hooked;
    private static List<Task>? _restoringBodies;
    private readonly GameWorld _world;
    private readonly List<JsonLootItemDescriptor> _loot = new();
    private readonly List<(LootableContainer Container, ItemDescriptor? Items, EDoorState State, float Angle)> _containers = new();
    private readonly List<MissionDoorSnapshot> _doors = new();
    private readonly List<MissionInteractionSnapshot> _interactions = new();

    internal static async Task SettleAsync(CancellationToken token)
    {
        var targets = Resources.FindObjectsOfTypeAll<WorldInteractiveObject>();
        var until = Time.realtimeSinceStartup + 8;
        while (targets.AsValueEnumerable().Any(t => t && t.gameObject.scene.isLoaded && t.DoorState == EDoorState.Interacting))
        {
            if (Time.realtimeSinceStartup >= until)
                throw new TimeoutException("A native door or container interaction did not finish before checkpoint capture.");
            await UniTask.Delay(50, delayType: DelayType.Realtime, cancellationToken: token);
        }
    }

    internal MissionWorldSnapshot(GameWorld world, Player player)
    {
        _world = world;
        foreach (var loot in world.LootList.AsValueEnumerable().OfType<LootItem>())
        {
            if (!loot || loot.Item == null)
                continue;
            JsonLootItem record;
            if (loot is Corpse corpse)
                record = new JsonCorpse
                {
                    Customization = corpse.Customization,
                    Side = corpse.Side,
                    ProfileID = corpse.PlayerProfileID,
                    Bones = corpse.GetTransformSync(),
                    IsZombieCorpse = corpse.IsZombieCorpse,
                };
            else
                record = new JsonLootItem();
            record.Id = loot.StaticId ?? loot.ItemId;
            record.Item = loot.Item;
            record.Position = loot.transform.position;
            record.Rotation = loot.transform.eulerAngles;
            record.ValidProfiles = loot.ValidProfiles;
            record.Shift = loot.Shift;
            record.PlatformId = -1;
            // Snapshot settled world positions; inherited throw velocity is not replayed.
            record.useGravity = false;
            record.randomRotation = false;
            _loot.Add(ItemBinarySerializer.SerializeJsonLootItem(record, player.SearchController));
        }
        foreach (var container in Resources.FindObjectsOfTypeAll<LootableContainer>())
        {
            if (!container || !container.gameObject.scene.IsValid() || !container.gameObject.scene.isLoaded)
                continue;
            _containers.Add(
                (
                    container,
                    container.ItemOwner?.RootItem == null
                        ? null
                        : ItemBinarySerializer.SerializeItem(container.ItemOwner.RootItem, player.SearchController),
                    container.DoorState,
                    container.CurrentAngle
                )
            );
            _interactions.Add(new(container));
        }
        foreach (var door in Resources.FindObjectsOfTypeAll<Door>())
            if (door && door.gameObject.scene.IsValid() && door.gameObject.scene.isLoaded)
            {
                _doors.Add(new(door));
                _interactions.Add(new(door));
            }
    }

    internal void Clear()
    {
        RequireWorld();
        _world.ClientBallisticCalculator?.ClearShots();
        _world.SharedBallisticsCalculator?.ClearShots();
        for (var index = _world.Grenades.Count - 1; index >= 0; index--)
        {
            var grenade = _world.Grenades.GetByIndex(index);
            if (!grenade)
                continue;
            grenade.CancelInvoke();
            grenade.StopAllCoroutines();
            grenade.gameObject.SetActive(false);
            _world.UnregisterGrenade(grenade);
            UnityEngine.Object.Destroy(grenade.gameObject);
        }
        foreach (var loot in _world.LootList.AsValueEnumerable().OfType<LootItem>().ToArray())
            if (loot)
                _world.DestroyLoot(loot);
        foreach (var saved in _containers)
        {
            if (!saved.Container)
                throw new InvalidOperationException("A checkpoint container was destroyed.");
            _world.LootList.Remove(saved.Container);
            if (saved.Container.ItemOwner != null)
                _world.ItemOwners.Remove(saved.Container.ItemOwner);
        }
    }

    internal async Task RestoreAsync(CancellationToken token)
    {
        RequireWorld();
        foreach (var interaction in _interactions)
            interaction.Restore();
        if (!_hooked)
        {
            new Harmony("com.wtt.campaigns.checkpoint-corpses").Patch(
                AccessTools.Method(typeof(Corpse), nameof(Corpse.InitBody)),
                postfix: new HarmonyMethod(typeof(MissionWorldSnapshot), nameof(TrackBody))
            );
            _hooked = true;
        }
        foreach (var saved in _containers)
        {
            if (!saved.Container)
                throw new InvalidOperationException("A checkpoint container is unavailable.");
            if (saved.Items != null)
                LootItem.CreateLootContainer(saved.Container, saved.Items.Deserialize(), saved.Container.name, _world, saved.Container.Id);
            else
                saved.Container.ItemOwner = null!;
            saved.Container.SetInitialSyncState(
                new WorldInteractiveObject.InteractiveObjectStatusInfo(saved.Container.Id, saved.State, saved.Angle)
            );
        }
        var bodies = new List<Task>();
        if (_restoringBodies != null)
            throw new InvalidOperationException("Another checkpoint world restoration is already active.");
        _restoringBodies = bodies;
        try
        {
            foreach (var saved in _loot)
            {
                token.ThrowIfCancellationRequested();
                var item = ItemBinarySerializer.DeserializeJsonLootItem(saved);
                if (item is JsonCorpse corpse)
                    _world.SpawnLootCorpse(corpse);
                else
                    _world.SpawnLootItem(item, false);
            }
        }
        finally
        {
            _restoringBodies = null;
        }
        var completion = Task.WhenAll(bodies);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        var timeout = UniTask.Delay(45000, delayType: DelayType.Realtime, cancellationToken: deadline.Token).AsTask();
        var finished = await Task.WhenAny(completion, timeout);
        deadline.Cancel();
        token.ThrowIfCancellationRequested();
        if (finished != completion)
            throw new TimeoutException("Native checkpoint corpses did not finish loading.");
        await completion;
        RequireWorld();
        foreach (var door in _doors)
        {
            door.Restore();
        }
        var player = _world.MainPlayer;
        var items = _world.LootList.AsValueEnumerable().OfType<LootItem>().Where(l => l && l.Item != null).ToArray();
        if (items.Length != _loot.Count || items.AsValueEnumerable().Select(l => l.ItemId).Distinct().Count() != items.Length)
            throw new InvalidOperationException("Restored world item ownership does not match the checkpoint.");
        foreach (var saved in _loot)
        {
            var restored = items.AsValueEnumerable().Single(l => l.ItemId == saved.Item.Id.ToString());
            MissionInventorySnapshot.RequireItems(saved.Item, ItemBinarySerializer.SerializeItem(restored.Item, player.SearchController));
        }
        foreach (var saved in _containers)
            MissionInventorySnapshot.RequireItems(
                saved.Items,
                ItemBinarySerializer.SerializeItem(saved.Container.ItemOwner?.RootItem, player.SearchController)
            );
    }

    private static void TrackBody(Task __result) => _restoringBodies?.Add(__result);

    private void RequireWorld()
    {
        if (!_world || !Singleton<GameWorld>.Instantiated || Singleton<GameWorld>.Instance != _world)
            throw new InvalidOperationException("The checkpoint belongs to an ended raid.");
    }
}
