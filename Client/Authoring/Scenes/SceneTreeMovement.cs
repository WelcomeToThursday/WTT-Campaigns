using EFT.Interactive;
using HarmonyLib;
using UnityEngine;

namespace WTT.Campaigns.Client.Authoring.Scenes;

internal static class SceneTreeMovement
{
    private static readonly System.Reflection.FieldInfo Sources = AccessTools.Field(typeof(TreeInteractive), "_sources");
    private static readonly System.Reflection.FieldInfo Players = AccessTools.Field(typeof(TreeInteractive), "_colliderPlayers");

    // Moving or hiding a trigger does not reliably emit exits for its old occupants.
    internal static void ReleaseContacts(TreeInteractive[] trees)
    {
        foreach (var tree in trees)
        {
            if (!tree || Sources.GetValue(tree) is not Dictionary<Collider, BetterSource> sources)
                continue;
            var contacts = new Collider[sources.Count];
            sources.Keys.CopyTo(contacts, 0);
            foreach (var collider in contacts)
                if (collider)
                    tree.OnTriggerExit(collider);
                else if (sources.TryGetValue(collider, out var source))
                {
                    if (source)
                        source.Release();
                    sources.Remove(collider);
                }
            if (Players.GetValue(tree) is System.Collections.IDictionary players)
                players.Clear();
        }
    }
}
