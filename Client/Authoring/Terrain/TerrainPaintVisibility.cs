using HarmonyLib;
using UnityEngine;

namespace WTT.Campaigns.Client.Authoring.TerrainEditing;

// Baked terrain proxy meshes cannot show runtime splat edits. Edited tiles use Unity's
// native terrain LOD renderer until their original data is restored.
internal static class TerrainPaintVisibility
{
    private sealed class Original
    {
        internal UnityEngine.Terrain Terrain = null!;
        internal bool Heightmap;
        internal readonly List<(TerrainLod Lod, bool Visible, bool Proxy)> Lods = new();
    }

    private static readonly Dictionary<UnityEngine.Terrain, Original> Owned = new();
    private static bool _patched;

    internal static void Acquire(UnityEngine.Terrain terrain)
    {
        if (Owned.ContainsKey(terrain))
            return;
        if (!_patched)
        {
            new Harmony("wtt.campaigns.terrain-visibility").Patch(
                AccessTools.PropertySetter(typeof(TerrainLod), "TerrainIsVisible"),
                prefix: new HarmonyMethod(typeof(TerrainPaintVisibility), nameof(Visible))
            );
            _patched = true;
        }
        var original = new Original { Terrain = terrain, Heightmap = terrain.drawHeightmap };
        foreach (var lod in Resources.FindObjectsOfTypeAll<TerrainLod>())
            if (lod && lod._terrain == terrain && lod._terrainLod)
                original.Lods.Add((lod, lod.TerrainIsVisible, lod._terrainLod.activeSelf));
        Owned.Add(terrain, original);
        foreach (var entry in original.Lods)
            entry.Lod.TerrainIsVisible = true;
        terrain.drawHeightmap = true;
    }

    internal static void Refresh(UnityEngine.Terrain terrain)
    {
        if (!terrain || !Owned.TryGetValue(terrain, out var original))
            return;
        foreach (var entry in original.Lods)
            if (entry.Lod && entry.Lod._terrainLod)
                entry.Lod.TerrainIsVisible = true;
        terrain.drawHeightmap = true;
    }

    private static void Visible(TerrainLod __instance, ref bool __0)
    {
        if (__instance._terrain && Owned.ContainsKey(__instance._terrain))
            __0 = true;
    }

    internal static void Release(UnityEngine.Terrain terrain)
    {
        if (!Owned.TryGetValue(terrain, out var original))
            return;
        Owned.Remove(terrain);
        foreach (var entry in original.Lods)
            if (entry.Lod && entry.Lod._terrainLod)
            {
                entry.Lod.TerrainIsVisible = entry.Visible;
                entry.Lod._terrainLod.SetActive(entry.Proxy);
            }
        if (terrain)
            terrain.drawHeightmap = original.Heightmap;
    }
}
