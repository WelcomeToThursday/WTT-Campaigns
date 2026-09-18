using System.Collections;
using System.Reflection;
using EFT;
using EFT.Ballistics;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using UnityEngine;
using WTT.Campaigns.Shared.Missions;
using ZLinq;

namespace WTT.Campaigns.Client.Missions;

/// <summary>Preserves native health/effect and physical object identities, including active stimulator clocks.</summary>
internal sealed class MissionActorSnapshot
{
    private readonly Player _original;
    private readonly ActiveHealthController _health;
    private readonly CheckpointObjectState _state;
    private readonly Vector3 _position;
    private readonly Vector2 _rotation;
    private readonly byte[] _healthBytes;
    private readonly Profile _profile;
    private readonly InventoryController _inventory;
    private readonly PhysicalBase _physical;
    private readonly object _skills;
    private readonly List<(Item Item, object[] Components)> _items = new();
    internal Vector3 Position => _position;

    internal MissionActorSnapshot(Player player)
    {
        _original = player;
        _profile = player.Profile; _inventory = player.InventoryController; _physical = player.Physical;
        _skills = player.Profile.Skills;
        _health = player.ActiveHealthController ?? throw new InvalidOperationException("Checkpoint actor health is unavailable.");
        if (!_health.IsAlive) throw new InvalidOperationException("Dead actors must be captured as native corpses.");
        _position = player.Transform.position;
        _rotation = player.Rotation;
        _healthBytes = _health.SerializeState();
        foreach (var item in player.Profile.Inventory.GetPlayerItems(EPlayerItems.Equipment | EPlayerItems.QuestItems))
            _items.Add((item, item.Components.AsValueEnumerable().Cast<object>().ToArray()));
        _state = new CheckpointObjectState(new object[] { _health, player.Physical, player.Profile.Skills }, OwnsHealthState);
    }

    internal static bool OwnsHealthState(object value)
    {
        if (value is UnityEngine.Object or Player or Profile or Item or IItemOwner or IClientSession or Task
            or CancellationTokenSource or System.IO.Stream or MemberInfo) return false;
        var type = value.GetType();
        if (type.Name.Contains("Settings") || type.Name.Contains("Template") || type.Name.Contains("Pool")
            || type.Name.Contains("Logger") || type.Name.Contains("Disposable")) return false;
        return value is IList or IDictionary || type.IsGenericType && type.GetGenericTypeDefinition() == typeof(HashSet<>)
            || type.Assembly == typeof(Player).Assembly || type.Namespace?.StartsWith("Diz.Binding", StringComparison.Ordinal) == true;
    }

    internal void Restore(Player player, BotOwner? originalBot = null, BotOwner? replacementBot = null)
    {
        var health = player.ActiveHealthController ?? throw new InvalidOperationException("Restored actor health is unavailable.");
        var replacements = new List<KeyValuePair<object, object>>();
        var items = player.Profile.Inventory.GetPlayerItems(EPlayerItems.Equipment | EPlayerItems.QuestItems)
            .AsValueEnumerable().ToDictionary(i => i.Id.ToString(), i => i);
        foreach (var saved in _items)
        {
            if (!items.TryGetValue(saved.Item.Id.ToString(), out var item))
                throw new InvalidOperationException("A checkpoint health-effect item was not restored.");
            replacements.Add(new(saved.Item, item));
            foreach (var component in saved.Components)
            {
                var current = item.Components.AsValueEnumerable().FirstOrDefault(c => c.GetType() == component.GetType());
                if (current == null) throw new InvalidOperationException("A checkpoint item component was not restored.");
                replacements.Add(new(component, current));
            }
        }
        // EFT may recycle the same Player component while replacing its controllers.
        // Map every captured controller independently, even when the Unity object is reused.
        replacements.Add(new(_original, player));
        replacements.Add(new(_health, health));
        replacements.Add(new(_profile, player.Profile));
        replacements.Add(new(_inventory, player.InventoryController));
        replacements.Add(new(_physical, player.Physical));
        replacements.Add(new(_skills, player.Profile.Skills));
        if (!ReferenceEquals(originalBot, null) && !ReferenceEquals(replacementBot, null)) replacements.Add(new(originalBot!, replacementBot!));
        // Unwind the abandoned attempt through native effect lifecycle callbacks.
        // Raw field restoration alone leaves HUD, movement and camera observers injured.
        foreach (var effect in health.Effects.AsValueEnumerable().ToArray()) effect.ForceRemove();
        _state.Restore(replacements);
        player.Teleport(_position);
        player.Rotation = _rotation;
        // Compare the native wire representation too: omitted body/effect data must fail closed.
        if (!health.IsAlive || !Equal(_healthBytes, health.SerializeState()))
            throw new InvalidOperationException("Native health or effect restoration did not match the accepted checkpoint.");
        RefreshObservers(player, health);
        if (!Equal(_healthBytes, health.SerializeState()))
            throw new InvalidOperationException("Native health changed while refreshing checkpoint observers.");
    }

    private static void Notify(ActiveHealthController health, string name, params object[] arguments)
    {
        var field = typeof(ActiveHealthController).GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(ActiveHealthController).FullName, name);
        (field.GetValue(health) as Delegate)?.DynamicInvoke(arguments);
    }

    private static void RefreshObservers(Player player, ActiveHealthController health)
    {
        foreach (var effect in health.Effects.AsValueEnumerable().ToArray())
        {
            Notify(health, nameof(ActiveHealthController.EffectAddedEvent), effect);
            if (effect.Active) Notify(health, nameof(ActiveHealthController.EffectStartedEvent), effect);
            else if (effect.Residual) Notify(health, nameof(ActiveHealthController.EffectResidualEvent), effect);
        }
        foreach (EBodyPart part in Enum.GetValues(typeof(EBodyPart)))
        {
            if (part == EBodyPart.Common) continue;
            Notify(health, nameof(ActiveHealthController.HealthChangedEvent), part, 0f, default(DamageInfo));
            player.UpdateConditionsAfterBodyPartStateChanged(part);
        }
        Notify(health, nameof(ActiveHealthController.EnergyChangedEvent), 0f);
        Notify(health, nameof(ActiveHealthController.HydrationChangedEvent), 0f);
        player.UpdateSpeedLimitByHealth();
        player.UpdateArmsCondition();
    }

    private static bool Equal(byte[] left, byte[] right)
    {
        if (left.Length != right.Length) return false;
        for (var index = 0; index < left.Length; index++) if (left[index] != right[index]) return false;
        return true;
    }
}
