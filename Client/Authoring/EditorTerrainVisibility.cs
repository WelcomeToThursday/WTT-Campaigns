using UnityEngine;

namespace WTT.Campaigns.Client.Authoring;

// Terrain draw flags are controlled by player collider zones, independently of
// PerfectCulling's camera. Free flight must not inherit the stationary player's zone.
internal sealed class EditorTerrainVisibility : IDisposable
{
    private readonly Dictionary<Terrain, (bool Heightmap, bool Foliage)> _terrains = new();
    private readonly Dictionary<TerrainLod, (bool Visible, bool ProxyActive)> _lods = new();

    internal void Observe(Terrain terrain)
    {
        if (terrain && !_terrains.ContainsKey(terrain))
            _terrains.Add(terrain, (terrain.drawHeightmap, terrain.drawTreesAndFoliage));
    }

    internal void Observe(TerrainLod lod)
    {
        if (!lod || !lod._terrain || !lod._terrainLod || _lods.ContainsKey(lod))
            return;
        Observe(lod._terrain);
        _lods.Add(lod, (lod.TerrainIsVisible, lod._terrainLod.activeSelf));
    }

    internal void Show()
    {
        foreach (var lod in _lods.Keys)
            if (lod && lod._terrain && lod._terrainLod && !lod.TerrainIsVisible)
                lod.TerrainIsVisible = true;
        foreach (var terrain in _terrains.Keys)
            if (terrain && !terrain.drawHeightmap)
                terrain.drawHeightmap = true;
    }

    public void Dispose()
    {
        foreach (var entry in _lods)
            if (entry.Key && entry.Key._terrain && entry.Key._terrainLod)
            {
                entry.Key.TerrainIsVisible = entry.Value.Visible;
                entry.Key._terrainLod.SetActive(entry.Value.ProxyActive);
            }
        foreach (var entry in _terrains)
            if (entry.Key)
            {
                entry.Key.drawHeightmap = entry.Value.Heightmap;
                entry.Key.drawTreesAndFoliage = entry.Value.Foliage;
            }
        _lods.Clear();
        _terrains.Clear();
    }
}
