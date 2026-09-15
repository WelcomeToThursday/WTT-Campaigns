// Mirrors the installed TerrainLod setter's visibility/proxy side effects.
internal sealed class TerrainLod : UnityEngine.Component
{
    public UnityEngine.Terrain _terrain = null!;
    public UnityEngine.GameObject _terrainLod = null!;
    private bool _visible;
    public bool TerrainIsVisible
    {
        get => _visible;
        set
        {
            _visible = value;
            _terrain.drawHeightmap = value;
            _terrain.drawTreesAndFoliage = value;
            _terrainLod.SetActive(!value);
        }
    }
}
