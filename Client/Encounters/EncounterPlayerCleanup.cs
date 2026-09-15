using EFT;
using EFT.AssetsManager;

namespace WTT.Campaigns.Client.Encounters;

internal static class EncounterPlayerCleanup
{
    internal static void Dispose(Player? player)
    {
        if (!player)
            return;
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
}
