using Comfort.Common;
using Cysharp.Threading.Tasks;
using Diz.Jobs;
using EFT;
using EFT.AssetsManager;
using EFT.HealthSystem;
using EFT.Interactive;
using EFT.InventoryLogic;
using Newtonsoft.Json;
using SPT.Common.Http;
using WTT.Campaigns.Shared.Authoring;
using ZLinq;

namespace WTT.Campaigns.Client.Authoring.Preview;

/// <summary>Temporary gear is installed into existing native slots, preserving their observers.</summary>
internal sealed class EditorPreviewPlayer
{
    private readonly Player _player;
    private readonly GameWorld? _world;
    private readonly Dictionary<EquipmentSlot, Item?> _original = new();
    private readonly Dictionary<EBoundItem, Item> _originalBindings = new();
    private readonly HashSet<string> _temporaryItems = new(StringComparer.Ordinal);
    private readonly HashSet<string> _existingLoot = new(StringComparer.Ordinal);
    private bool _restored;

    internal EditorPreviewPlayer(Player player)
    {
        _player = player;
        _world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
        if (_world)
            foreach (var loot in _world!.LootList.AsValueEnumerable().OfType<LootItem>())
                if (loot)
                    _existingLoot.Add(loot.ItemId);
    }

    internal async Task Equip(CancellationToken token, bool useProfileKit = false, Action<string>? report = null)
    {
        using var loading = UI.NativeLoadingStatus.Begin("Preparing playtest equipment…");
        void Stage(string text)
        {
            Plugin.LogInfo("Playtest equipment: " + text);
            report?.Invoke(text);
        }
        Stage("Requesting " + (useProfileKit ? "current character gear" : "default gear"));
        var payload = JsonConvert.SerializeObject(
            new EditorPreviewGearRequest { SessionId = EditorMode.SessionId, UseProfileKit = useProfileKit }
        );
        var response =
            JsonConvert.DeserializeObject<EditorPreviewGearResponse>(
                await RequestHandler.PostJsonAsync("/wtt-campaigns/editor/preview-gear", payload)
            ) ?? throw new InvalidOperationException("The playtest equipment response was empty.");
        token.ThrowIfCancellationRequested();
        if (!string.IsNullOrEmpty(response.Error))
            throw new InvalidOperationException(response.Error);
        Stage("Building " + response.Slots.Count + " equipment slots");
        var prepared = new Dictionary<EquipmentSlot, Item>();
        var resources = new HashSet<ResourceKey>();
        foreach (var entry in response.Slots)
        {
            if (!Enum.TryParse<EquipmentSlot>(entry.Slot, out var slot) || !Enum.IsDefined(typeof(EquipmentSlot), slot))
                throw new InvalidOperationException("Unsupported equipped slot: " + entry.Slot);
            var item = ItemPreviewClient.Build(new ItemPreviewJob { Items = entry.Items }, new(), out _);
            prepared.Add(slot, item);
            foreach (var record in entry.Items)
            {
                _temporaryItems.Add(record.Id);
                var template = Singleton<ItemFactory>.Instance.ItemTemplates[record.Template];
                if (template.Prefab != null && !string.IsNullOrWhiteSpace(template.Prefab.path))
                    resources.Add(template.Prefab);
                if (template.UsePrefab != null && !string.IsNullOrWhiteSpace(template.UsePrefab.path))
                    resources.Add(template.UsePrefab);
            }
        }
        var factory = Singleton<ObjectsFactory>.Instance;
        foreach (var resource in resources)
            if (!factory.EasyAssets.System.Nodes.ContainsKey(resource.path))
                throw new InvalidOperationException("Playtest equipment bundle is not installed: " + resource.path);
        var poolStage = "Loading " + resources.Count + " equipment resources";
        Stage(poolStage);
        var progress = new Progress<InitLevelProgress>(value =>
        {
            if (token.IsCancellationRequested || _restored)
                return;
            var next = "Equipment " + value.Stage + " · " + (value.Progress * 100).ToString("0") + "%";
            if (next == poolStage)
                return;
            poolStage = next;
            Stage(next);
        });
        await PreviewLoadGuard.Run(
            loadToken =>
                factory.LoadBundlesAndCreatePools(
                    ObjectsFactory.PoolsCategory.Raid,
                    ObjectsFactory.AssemblyType.Local,
                    resources.AsValueEnumerable().ToArray(),
                    JobYieldPriority.Immediate,
                    progress,
                    loadToken
                ),
            timerToken => UniTask.Delay(45000, delayType: DelayType.Realtime, cancellationToken: timerToken).AsTask(),
            token,
            () =>
                "Playtest equipment timed out after 45 seconds at "
                + poolStage
                + ". Check the client asset-bundle log for a missing or failed item model."
        );
        token.ThrowIfCancellationRequested();
        if (_restored || !_player || !Singleton<GameWorld>.Instantiated || Singleton<GameWorld>.Instance != _world)
            throw new OperationCanceledException("The playtest world ended while loading equipment.");
        Stage("Preparing hands");
        await EmptyHands(_player);
        token.ThrowIfCancellationRequested();
        Stage("Equipping temporary gear");
        foreach (var binding in _player.InventoryController.FastAccess.BoundItems)
            _originalBindings.Add(binding.Key, binding.Value);
        foreach (EquipmentSlot slot in Enum.GetValues(typeof(EquipmentSlot)))
        {
            var native = _player.Equipment.GetSlot(slot);
            if (native != null)
                _original.Add(slot, native.ContainedItem);
        }
        foreach (var entry in _original)
            Replace(entry.Key, null);
        foreach (var entry in prepared)
            Replace(entry.Key, entry.Value);
        // The native search controller initialized before these fresh item
        // identities existed. Repeat its own equipped-content initialization.
        if (_player.SearchController is ActiveSearchController search)
            search.UncoverContent(_player.Equipment);
        else
            throw new InvalidOperationException("The native equipment search controller is unavailable.");
        var boundItems = _player.InventoryController.FastAccess.BoundItems;
        boundItems.Clear();
        foreach (var binding in response.FastPanel)
        {
            var name = binding.Key.StartsWith("Item", StringComparison.Ordinal) ? binding.Key : "Item" + binding.Key;
            if (
                Enum.TryParse<EBoundItem>(name, out var key)
                && Enum.IsDefined(typeof(EBoundItem), key)
                && _player.Equipment.TryFindItem(binding.Value, out var item)
            )
                boundItems[key] = item;
        }
        FreshHealth(_player);
        _player.RecalculateEquipmentParams();
        Stage("Ready");
    }

    internal void Arm()
    {
        foreach (
            var slot in new[]
            {
                EquipmentSlot.FirstPrimaryWeapon,
                EquipmentSlot.SecondPrimaryWeapon,
                EquipmentSlot.Holster,
                EquipmentSlot.Scabbard,
            }
        )
            if (_player.Equipment.GetSlot(slot)?.ContainedItem != null)
            {
                _player.SetSlotItem(slot, _ => { });
                break;
            }
    }

    private void Replace(EquipmentSlot key, Item? item)
    {
        var slot = _player.Equipment.GetSlot(key);
        if (slot.ContainedItem != null)
        {
            var old = slot.ContainedItem;
            var address = old.Parent;
            var removed = slot.RemoveItemWithoutRestrictions();
            if (removed.Failed)
                throw new InvalidOperationException(removed.Error.ToString());
            // Native immediate operations pair Begin/Succeed after mutation
            // so ItemController can retire the matching inventory activity.
            _player.InventoryController.RaiseRemoveEvent(
                new RemoveItemEventArgs(old, address, CommandStatus.Begin, _player.InventoryController)
            );
            _player.InventoryController.RaiseRemoveEvent(
                new RemoveItemEventArgs(old, address, CommandStatus.Succeed, _player.InventoryController)
            );
        }
        if (item == null)
            return;
        var added = slot.AddWithoutRestrictions(item);
        if (added.Failed)
            throw new InvalidOperationException(added.Error.ToString());
        _player.InventoryController.RaiseAddEvent(
            new AddItemEventArgs(item, item.Parent, CommandStatus.Begin, _player.InventoryController)
        );
        _player.InventoryController.RaiseAddEvent(
            new AddItemEventArgs(item, item.Parent, CommandStatus.Succeed, _player.InventoryController)
        );
    }

    internal async Task Restore()
    {
        if (_restored)
            return;
        if (_player && _original.Count > 0)
        {
            foreach (var operation in _player.SearchController.SearchOperations.AsValueEnumerable().ToArray())
                _player.SearchController.StopSearching(operation.Item.Id);
            await EmptyHands(_player);
            if (_player)
            {
                foreach (var entry in _original)
                    Replace(entry.Key, null);
                foreach (var entry in _original)
                    Replace(entry.Key, entry.Value);
                var bindings = _player.InventoryController.FastAccess.BoundItems;
                bindings.Clear();
                foreach (var entry in _originalBindings)
                    bindings[entry.Key] = entry.Value;
                FreshHealth(_player);
                _player.RecalculateEquipmentParams();
                if (_player.SearchController is ActiveSearchController search)
                {
                    foreach (var item in search._knownItems.Keys.AsValueEnumerable().Where(i => _temporaryItems.Contains(i.Id)).ToArray())
                        search.ForgetItem(item);
                    search._searchedItems.RemoveWhere(i => _temporaryItems.Contains(i.Id));
                    search._discoveredItems.RemoveWhere(i => _temporaryItems.Contains(i.Id));
                    search._temporaryKnownItems.RemoveWhere(i => _temporaryItems.Contains(i.Id));
                }
            }
        }
        if (_world)
        {
            var world = _world!;
            // Includes split stacks and thrown/dropped items given new native identities.
            foreach (
                var loot in world
                    .LootList.AsValueEnumerable()
                    .OfType<LootItem>()
                    .Where(l => l && !_existingLoot.Contains(l.ItemId))
                    .ToArray()
            )
                world.DestroyLoot(loot);
        }
        _original.Clear();
        _originalBindings.Clear();
        _temporaryItems.Clear();
        _restored = true;
    }

    internal static void FreshHealth(Player player)
    {
        var health = player.ActiveHealthController;
        if (health == null)
            throw new InvalidOperationException("The player health controller is unavailable.");
        foreach (var effect in health.Effects.AsValueEnumerable().Where(e => e is not IPermanent).ToArray())
            effect.ForceRemove();
        foreach (EBodyPart part in Enum.GetValues(typeof(EBodyPart)))
            if (part != EBodyPart.Common)
                health.FullRestoreBodyPart(part);
        health.ChangeEnergy(health.Energy.Maximum - health.Energy.Current);
        health.ChangeHydration(health.Hydration.Maximum - health.Hydration.Current);
    }

    private static async Task EmptyHands(Player player)
    {
        if (!player || player.HandsIsEmpty)
            return;
        var ready = new TaskCompletionSource<bool>();
        player.SetEmptyHands(result => ready.TrySetResult(result.Succeed));
        using var timeoutLifetime = new CancellationTokenSource();
        var timeout = UniTask.Delay(5000, delayType: DelayType.Realtime, cancellationToken: timeoutLifetime.Token).AsTask();
        var completed = await Task.WhenAny(ready.Task, timeout);
        timeoutLifetime.Cancel();
        if (completed != ready.Task || !await ready.Task)
            throw new InvalidOperationException("Wait for the current hands operation before resetting the playtest.");
    }
}
