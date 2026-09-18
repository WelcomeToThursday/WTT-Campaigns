using Cysharp.Threading.Tasks;
using EFT;
using EFT.InventoryLogic;
using ZLinq;
using UnityEngine;

namespace WTT.Campaigns.Client.Missions;

/// <summary>Native item trees are rebuilt inside the existing observed equipment and quest containers.</summary>
internal sealed class MissionInventorySnapshot
{
    private readonly Dictionary<EquipmentSlot, ItemDescriptor?> _equipment = new();
    private readonly List<(ItemDescriptor Item, LocationInGrid Location)> _questItems = new();
    private readonly Dictionary<EBoundItem, string> _bindings = new();
    private readonly Dictionary<MongoID, int> _discardLimits;
    private readonly string _profileId;

    internal MissionInventorySnapshot(Player player)
    {
        _profileId = player.ProfileId;
        foreach (EquipmentSlot slot in Enum.GetValues(typeof(EquipmentSlot)))
        {
            var native = player.Equipment.GetSlot(slot);
            if (native != null)
                _equipment.Add(slot, native.ContainedItem == null ? null : ItemBinarySerializer.SerializeItem(native.ContainedItem, player.SearchController));
        }
        var quest = player.Profile.Inventory.QuestRaidItems;
        if (quest != null)
            foreach (var item in quest.Grid.Items)
            {
                var position = quest.Grid.GetItemLocation(item);
                _questItems.Add((ItemBinarySerializer.SerializeItem(item, player.SearchController), new LocationInGrid(position.x, position.y, position.r)));
            }
        foreach (var pair in player.InventoryController.FastAccess.BoundItems) _bindings.Add(pair.Key, pair.Value.Id);
        _discardLimits = new(player.Profile.Inventory.DiscardLimits);
    }

    internal static async Task EmptyHands(Player player, CancellationToken token)
    {
        foreach (var operation in player.SearchController.SearchOperations.AsValueEnumerable().ToArray())
            player.SearchController.StopSearching(operation.Item.Id);
        if (player.HandsIsEmpty) return;
        var ready = new TaskCompletionSource<bool>();
        player.SetEmptyHands(result => ready.TrySetResult(result.Succeed));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        var timeout = UniTask.Delay(5000, delayType: DelayType.Realtime, cancellationToken: deadline.Token).AsTask();
        var completed = await Task.WhenAny(ready.Task, timeout);
        deadline.Cancel();
        token.ThrowIfCancellationRequested();
        if (completed != ready.Task || !await ready.Task)
            throw new InvalidOperationException("The native hands operation did not finish during checkpoint restoration.");
    }

    internal static async Task SettleHands(Player player, CancellationToken token)
    {
        foreach (var operation in player.SearchController.SearchOperations.AsValueEnumerable().ToArray())
            player.SearchController.StopSearching(operation.Item.Id);
        var until = Time.realtimeSinceStartup + 8f;
        while (player.InventoryController.IsChangingWeapon || player.HandsController?.IsInInteractionStrictCheck() == true
            || player.HandsController is Player.FirearmController firearm && firearm.IsInReloadOperation())
        {
            if (Time.realtimeSinceStartup >= until) throw new TimeoutException("The hands operation did not settle before checkpoint capture.");
            await UniTask.Delay(25, delayType: DelayType.Realtime, cancellationToken: token);
        }
    }

    internal static async Task RestoreHands(Player player, string itemId, CancellationToken token)
    {
        if (itemId.Length == 0) return;
        var item = player.Profile.Inventory.GetPlayerItems(EPlayerItems.Equipment)
            .AsValueEnumerable().FirstOrDefault(i => i.Id.ToString() == itemId)
            ?? throw new InvalidOperationException("The checkpoint's held item was not restored.");
        var ready = new TaskCompletionSource<bool>();
        player.SetInHands(item, result => ready.TrySetResult(result.Succeed));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        var timeout = UniTask.Delay(8000, delayType: DelayType.Realtime, cancellationToken: deadline.Token).AsTask();
        var completed = await Task.WhenAny(ready.Task, timeout);
        deadline.Cancel(); token.ThrowIfCancellationRequested();
        if (completed != ready.Task || !await ready.Task || player.HandsController?.Item?.Id.ToString() != itemId)
            throw new InvalidOperationException("The checkpoint's held item could not be equipped.");
    }

    internal void Clear(Player player)
    {
        RequirePlayer(player);
        foreach (var slot in _equipment.Keys) Replace(player, slot, null);
        var quest = player.Profile.Inventory.QuestRaidItems;
        if (quest != null)
            foreach (var item in quest.Grid.Items.AsValueEnumerable().ToArray())
            {
                var address = item.Parent;
                var result = quest.Grid.RemoveWithoutRestrictions(item);
                if (result.Failed) throw new InvalidOperationException(result.Error.ToString());
                player.InventoryController.RaiseRemoveEvent(new RemoveItemEventArgs(item, address, CommandStatus.Begin, player.InventoryController));
                player.InventoryController.RaiseRemoveEvent(new RemoveItemEventArgs(item, address, CommandStatus.Succeed, player.InventoryController));
            }
        player.InventoryController.FastAccess.BoundItems.Clear();
    }

    internal void Restore(Player player)
    {
        RequirePlayer(player);
        if (_equipment.Keys.AsValueEnumerable().Any(s => player.Equipment.GetSlot(s).ContainedItem != null))
            throw new InvalidOperationException("Inventory ownership must be cleared before rebuilding checkpoint items.");
        foreach (var pair in _equipment) Replace(player, pair.Key, pair.Value?.Deserialize());
        var quest = player.Profile.Inventory.QuestRaidItems;
        if (_questItems.Count > 0 && quest == null) throw new InvalidOperationException("The native quest inventory is unavailable.");
        foreach (var saved in _questItems)
        {
            var item = saved.Item.Deserialize();
            var result = quest!.Grid.AddItemWithoutRestrictions(item, new LocationInGrid(saved.Location.x, saved.Location.y, saved.Location.r));
            if (result.Failed) throw new InvalidOperationException(result.Error.ToString());
            player.InventoryController.RaiseAddEvent(new AddItemEventArgs(item, item.Parent, CommandStatus.Begin, player.InventoryController));
            player.InventoryController.RaiseAddEvent(new AddItemEventArgs(item, item.Parent, CommandStatus.Succeed, player.InventoryController));
        }
        var bindings = player.InventoryController.FastAccess.BoundItems;
        bindings.Clear();
        foreach (var pair in _bindings)
            if (player.Equipment.TryFindItem(pair.Value, out var item)) bindings.Add(pair.Key, item);
        var limits = player.Profile.Inventory.DiscardLimits;
        limits.Clear();
        foreach (var pair in _discardLimits) limits.Add(pair.Key, pair.Value);
        player.RecalculateEquipmentParams();
        foreach (var pair in _equipment)
            RequireItems(pair.Value, ItemBinarySerializer.SerializeItem(player.Equipment.GetSlot(pair.Key).ContainedItem, player.SearchController));
        foreach (var saved in _questItems)
        {
            var item = quest!.Grid.Items.AsValueEnumerable().Single(i => i.Id == saved.Item.Id);
            RequireItems(saved.Item, ItemBinarySerializer.SerializeItem(item, player.SearchController));
            var location = quest.Grid.GetItemLocation(item);
            if (location.x != saved.Location.x || location.y != saved.Location.y || location.r != saved.Location.r)
                throw new InvalidOperationException("Restored quest inventory location does not match the checkpoint.");
        }
    }

    internal static void RequireItems(ItemDescriptor? expected, ItemDescriptor? actual)
    {
        var before = Newtonsoft.Json.Linq.JToken.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(expected, EftJsonConverters.Converters));
        var after = Newtonsoft.Json.Linq.JToken.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(actual, EftJsonConverters.Converters));
        if (!Newtonsoft.Json.Linq.JToken.DeepEquals(before, after))
            throw new InvalidOperationException("Restored native items, ammunition or condition do not match the accepted checkpoint.");
    }

    private void RequirePlayer(Player player)
    {
        if (!player || player.ProfileId != _profileId)
            throw new InvalidOperationException("The inventory checkpoint belongs to another player.");
    }

    private static void Replace(Player player, EquipmentSlot key, Item? item)
    {
        var slot = player.Equipment.GetSlot(key);
        if (slot.ContainedItem != null)
        {
            var old = slot.ContainedItem;
            var address = old.Parent;
            var result = slot.RemoveItemWithoutRestrictions();
            if (result.Failed) throw new InvalidOperationException(result.Error.ToString());
            player.InventoryController.RaiseRemoveEvent(new RemoveItemEventArgs(old, address, CommandStatus.Begin, player.InventoryController));
            player.InventoryController.RaiseRemoveEvent(new RemoveItemEventArgs(old, address, CommandStatus.Succeed, player.InventoryController));
        }
        if (item == null) return;
        var added = slot.AddWithoutRestrictions(item);
        if (added.Failed) throw new InvalidOperationException(added.Error.ToString());
        player.InventoryController.RaiseAddEvent(new AddItemEventArgs(item, item.Parent, CommandStatus.Begin, player.InventoryController));
        player.InventoryController.RaiseAddEvent(new AddItemEventArgs(item, item.Parent, CommandStatus.Succeed, player.InventoryController));
    }
}
