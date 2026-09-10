using System.Runtime.CompilerServices;
using Comfort.Common;
using EFT;
using EFT.Interactive;
using UnityEngine;

namespace WTT.Campaigns.Client.Patches.Movement;

internal static class BushOccupancy
{
    private static readonly ConditionalWeakTable<MovementContext, HashSet<(TreeInteractive Tree, Collider Collider)>> Entries = new();

    internal static bool Contains(MovementContext context)
    {
        if (!Entries.TryGetValue(context, out var entries))
        {
            return false;
        }

        entries.RemoveWhere(e => !e.Tree || !e.Tree.isActiveAndEnabled || !e.Collider || !e.Collider.enabled);
        return entries.Count != 0;
    }

    internal static bool RemoveInactive(MovementContext context)
    {
        return Entries.TryGetValue(context, out var entries)
            && entries.RemoveWhere(e => !e.Tree || !e.Tree.isActiveAndEnabled || !e.Collider || !e.Collider.enabled) != 0;
    }

    internal static void Update(TreeInteractive tree, Collider collider, bool enter)
    {
        if (!Plugin.SeasonalPlayer)
        {
            return;
        }

        var player = Singleton<GameWorld>.Instance.GetPlayerByCollider(collider);
        if (player == null || !ReferenceEquals(player, Plugin.Player))
        {
            return;
        }

        var context = player.MovementContext;
        var entries = Entries.GetOrCreateValue(context);
        bool changed = enter ? entries.Add((tree, collider)) : entries.Remove((tree, collider));
        if (changed)
        {
            context.RefreshObstacleRestrictions();
        }
    }
}
