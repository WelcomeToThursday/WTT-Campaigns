using UnityEngine;
using WTT.Campaigns.Client.Authoring;

namespace WTT.Campaigns.Tests;

internal static class EditorTerrainChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var terrain = new GameObject().AddComponent<Terrain>();
        var lod = new GameObject().AddComponent<TerrainLod>();
        lod._terrain = terrain;
        lod._terrainLod = new GameObject();
        lod.TerrainIsVisible = false; // Player is inside a terrain-disabling zone.
        using var visibility = new EditorTerrainVisibility();
        visibility.Observe(terrain);
        visibility.Observe(lod);
        visibility.Show();
        check(
            terrain.drawHeightmap && lod.TerrainIsVisible && !lod._terrainLod.activeSelf,
            "Free flight shows terrain hidden by the stationary player's trigger zone without duplicate LOD geometry"
        );
        lod.TerrainIsVisible = false; // Native culling writes again before the next camera pass.
        visibility.Show();
        check(terrain.drawHeightmap && !lod._terrainLod.activeSelf, "Terrain visibility is reapplied after native trigger culling updates");
        visibility.Observe(terrain);
        visibility.Observe(lod);
        visibility.Dispose();
        check(
            !terrain.drawHeightmap && !terrain.drawTreesAndFoliage && !lod.TerrainIsVisible && lod._terrainLod.activeSelf,
            "Walkthrough restores original terrain flags and proxy visibility after repeated discovery"
        );
        visibility.Dispose();
        check(!terrain.drawHeightmap, "Repeated terrain cleanup is harmless");

        terrain.drawHeightmap = false;
        terrain.drawTreesAndFoliage = true;
        visibility.Observe(terrain);
        visibility.Show();
        check(terrain.drawHeightmap && terrain.drawTreesAndFoliage, "Standalone terrain rendering preserves independent foliage");
        visibility.Dispose();
        check(!terrain.drawHeightmap && terrain.drawTreesAndFoliage, "Standalone terrain restores independent draw flags");
        visibility.Observe(lod);
        UnityEngine.Object.Destroy(lod._terrainLod);
        UnityEngine.Object.Destroy(terrain);
        visibility.Show();
        visibility.Dispose();
        check(true, "Map unload handles destroyed terrain and LOD proxies safely");
    }
}
