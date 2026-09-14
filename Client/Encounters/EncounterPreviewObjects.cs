using Comfort.Common;
using EFT;
using UnityEngine;

namespace WTT.Campaigns.Client.Encounters;

/// <summary>Owns additions to the exclusive editor world, never a later singleton world.</summary>
internal sealed class EncounterPreviewObjects
{
    private readonly GameWorld _world;
    private readonly HashSet<IKillable> _existingLoot = new();
    private readonly HashSet<Throwable> _existingGrenades = new();
    private readonly HashSet<IKillable> _pendingLoot = new();
    private readonly HashSet<Throwable> _pendingGrenades = new();

    internal EncounterPreviewObjects()
    {
        if (!Singleton<GameWorld>.Instantiated)
            throw new InvalidOperationException("The editor world is unavailable.");
        _world = Singleton<GameWorld>.Instance;
        foreach (var loot in _world.LootList)
            _existingLoot.Add(loot);
        for (var index = 0; index < _world.Grenades.Count; index++)
            _existingGrenades.Add(_world.Grenades.GetByIndex(index));
    }

    internal void Reset()
    {
        if (!_world)
            return;
        var failures = new List<Exception>();
        // Stop fuses before disposing bots/items. Never call grenade Explosion/OnExplosion.
        for (var index = _world.Grenades.Count - 1; index >= 0; index--)
        {
            var grenade = _world.Grenades.GetByIndex(index);
            if (grenade && !_existingGrenades.Contains(grenade))
                _pendingGrenades.Add(grenade);
        }
        foreach (var grenade in new List<Throwable>(_pendingGrenades))
        {
            if (!grenade)
            {
                _pendingGrenades.Remove(grenade);
                continue;
            }
            try
            {
                grenade.CancelInvoke();
                grenade.StopAllCoroutines();
                grenade.gameObject.SetActive(false);
                _world.UnregisterGrenade(grenade);
                UnityEngine.Object.Destroy(grenade.gameObject);
                _pendingGrenades.Remove(grenade);
            }
            catch (Exception error)
            {
                failures.Add(error);
            }
        }
        try
        {
            _world.SharedBallisticsCalculator?.ClearShots();
            _world.ClientBallisticCalculator?.ClearShots();
        }
        catch (Exception error)
        {
            failures.Add(error);
        }
        // Native DestroyLoot removes item-owner/net registries and invokes corpse/item Kill.
        foreach (var loot in _world.LootList)
            if (!_existingLoot.Contains(loot))
                _pendingLoot.Add(loot);
        foreach (var loot in new List<IKillable>(_pendingLoot))
        {
            try
            {
                _world.DestroyLoot(loot);
                _pendingLoot.Remove(loot);
            }
            catch (Exception error)
            {
                failures.Add(error);
            }
        }
        if (failures.Count > 0)
            throw new AggregateException("Preview object cleanup remains pending.", failures);
    }
}
