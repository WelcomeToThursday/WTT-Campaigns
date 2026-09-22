using Comfort.Common;
using EFT;
using EFT.AssetsManager;
using HarmonyLib;

namespace WTT.Campaigns.Client.Encounters;

internal static class EncounterPlayerCleanup
{
    private static readonly System.Reflection.FieldInfo NativeBots =
        AccessTools.Field(typeof(LocalGame), "_bots") ?? throw new MissingFieldException(typeof(LocalGame).FullName, "_bots");

    internal static void Dispose(Player? player)
    {
        if (!player)
            return;
        DetachFromRaid(player!);
        var root = player.gameObject;
        var pool = root.GetComponent<PlayerPoolObject>();
        if (pool && pool.IsInPool)
            return;
        var corpse = root.GetComponent<EFT.Interactive.Corpse>();
        var world = player.GameWorld;
        player.Dispose();
        if (corpse)
        {
            // Corpses share the player root. DestroyLoot unregisters the corpse and
            // returns that root to the pool; never schedule Destroy on it beforehand.
            if (!world)
                throw new InvalidOperationException("The encounter corpse world is unavailable for cleanup.");
            world.DestroyLoot(corpse);
        }
        else if (root)
        {
            // Native pooling cleans runtime components while retaining the cached rig.
            AssetPoolObject.ReturnToPool(root, true);
        }
    }

    private static void DetachFromRaid(Player player)
    {
        if (!Singleton<AbstractGame>.Instantiated || Singleton<AbstractGame>.Instance is not LocalGame game)
            return;
        var bots = (Dictionary<string, Player>)NativeBots.GetValue(game);
        // Only retire this owned instance. A late callback must not remove a
        // replacement bot with the same profile, or any native player entry.
        if (bots.TryGetValue(player.ProfileId, out var registered) && ReferenceEquals(registered, player))
            bots.Remove(player.ProfileId);
    }
}
