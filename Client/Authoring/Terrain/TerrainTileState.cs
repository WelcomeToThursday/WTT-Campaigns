using Cysharp.Threading.Tasks;
using GPUInstancer;
using HarmonyLib;
using UnityEngine;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Client.Authoring.TerrainEditing;

// Owns one tile's mutable data. Native asset objects are never painted in place.
internal sealed class TerrainTileState
{
    private static readonly System.Reflection.FieldInfo MixField = AccessTools.Field(typeof(TerrainBallistic), "_mixData");
    private static readonly System.Reflection.FieldInfo Initializing = AccessTools.Field(
        typeof(GPUInstancerTerrainManager),
        "initalizingInstances"
    );
    private static readonly System.Reflection.FieldInfo Replacing = AccessTools.Field(
        typeof(GPUInstancerTerrainManager),
        "replacingInstances"
    );
    internal readonly TerrainTile Tile;
    private readonly TerrainData _original,
        _data;
    private readonly TerrainCollider? _collider;
    private readonly TerrainData? _collisionData,
        _ballisticData;
    private readonly TerrainTextureMixData? _originalMix;
    private TerrainTextureMixData? _mix;
    private readonly Material? _material,
        _splatMaterial;
    private Material? _ownedMaterial;
    private Texture2D[]? _controls;
    private Color[][]? _originalControls;
    private readonly Dictionary<GPUInstancerDetailManager, List<int[,]>> _grassOriginal = new();
    private readonly Dictionary<GPUInstancerDetailManager, List<float[,]>> _grass = new();
    private readonly Dictionary<int, int[,]> _unityGrassOriginal = new();
    private readonly Dictionary<int, float[,]> _unityGrass = new();
    private bool _grassChanged,
        _texturesChanged;
    private bool _textureDirty;
    private bool _disposed;

    internal TerrainTileState(TerrainTile tile)
    {
        Tile = tile;
        _original = tile.Surface.terrainData;
        _collider = tile.Surface.GetComponent<TerrainCollider>();
        _collisionData = _collider ? _collider!.terrainData : null;
        _ballisticData = tile.Ballistic ? tile.Ballistic!.TerrainData : null;
        _originalMix = tile.Ballistic ? MixField.GetValue(tile.Ballistic) as TerrainTextureMixData : null;
        _material = tile.Surface.materialTemplate;
        _splatMaterial = tile.Splat ? tile.Splat!.matInstance : null;
        _data = UnityEngine.Object.Instantiate(_original);
        _data.name = _original.name;
        try
        {
            tile.Surface.terrainData = _data;
            if (_collider && _collisionData == _original)
                _collider!.terrainData = _data;
            if (tile.Ballistic && _ballisticData == _original)
                tile.Ballistic!.TerrainData = _data;
        }
        catch
        {
            if (tile.Surface)
                tile.Surface.terrainData = _original;
            if (_collider)
                _collider!.terrainData = _collisionData!;
            if (tile.Ballistic)
                tile.Ballistic!.TerrainData = _ballisticData!;
            UnityEngine.Object.Destroy(_data);
            throw;
        }
    }

    internal void EnsureTextures()
    {
        if (_texturesChanged)
            return;
        if (Tile.TextureError.Length > 0)
            throw new InvalidOperationException(Tile.TextureError);
        if (Tile.Custom)
        {
            _controls = new Texture2D[Tile.Controls.Length];
            _originalControls = new Color[Tile.Controls.Length][];
            for (var i = 0; i < _controls.Length; i++)
            {
                var source = Tile.Controls[i];
                if (!source || source.width != Tile.Width || source.height != Tile.Height)
                    throw new InvalidOperationException("Terrain control textures are missing or have different sizes.");
                _controls[i] = ReadableCopy(source);
                _originalControls[i] = _controls[i].GetPixels();
            }
            TerrainTile.SetControls(Tile.Splat!, _controls);
        }
        if (_material)
        {
            _ownedMaterial = UnityEngine.Object.Instantiate(_material);
            Tile.Surface.materialTemplate = _ownedMaterial;
            if (Tile.Splat)
            {
                Tile.Splat!.matInstance = _ownedMaterial;
                Tile.Splat.Sync();
            }
        }
        _mix = new TerrainTextureMixData
        {
            size = _original.size,
            alphamapWidth = Tile.Width,
            alphamapHeight = Tile.Height,
            data = new byte[Tile.Width, Tile.Height],
        };
        // The native lookup is [x,y], while Unity's alpha array is [y,x,layer].
        var sameMix = _originalMix != null && _originalMix.alphamapWidth == Tile.Width && _originalMix.alphamapHeight == Tile.Height;
        var alpha = Tile.Custom || sameMix ? null : _original.GetAlphamaps(0, 0, Tile.Width, Tile.Height);
        var weights = new float[Tile.Target.Textures];
        for (var y = 0; y < Tile.Height; y++)
        for (var x = 0; x < Tile.Width; x++)
        {
            if (sameMix)
            {
                _mix.data[x, y] = _originalMix!.data[x, y];
                continue;
            }
            for (var layer = 0; layer < weights.Length; layer++)
                weights[layer] = alpha == null ? _originalControls![layer / 4][y * Tile.Width + x][layer % 4] : alpha[y, x, layer];
            _mix.data[x, y] = (byte)MapTerrainPainting.Dominant(weights);
        }
        MixField.SetValue(Tile.Ballistic, _mix);
        TerrainPaintVisibility.Acquire(Tile.Surface);
        _texturesChanged = true;
    }

    internal void StampTexture(MapTerrainStroke stroke, SpatialVector point)
    {
        EnsureTextures();
        var rect = Region(point, stroke.Radius, Tile.Width, Tile.Height);
        if (rect.width == 0 || rect.height == 0)
            return;
        var count = Tile.Target.Textures;
        var current = new float[count];
        var original = new float[count];
        var alpha = Tile.Custom ? null : _data.GetAlphamaps(rect.x, rect.y, rect.width, rect.height);
        var baseline = Tile.Custom ? null : _original.GetAlphamaps(rect.x, rect.y, rect.width, rect.height);
        var colors = new Color[_controls?.Length ?? 0][];
        for (var i = 0; i < colors.Length; i++)
            colors[i] = _controls![i].GetPixels(rect.x, rect.y, rect.width, rect.height);
        for (var y = 0; y < rect.height; y++)
        for (var x = 0; x < rect.width; x++)
        {
            var weight = MapTerrainPainting.Weight(
                (rect.x + x + .5f) * Tile.Target.Width / Tile.Width,
                (rect.y + y + .5f) * Tile.Target.Depth / Tile.Height,
                point,
                stroke
            );
            if (weight <= 0)
                continue;
            for (var layer = 0; layer < count; layer++)
            {
                current[layer] = alpha == null ? colors[layer / 4][y * rect.width + x][layer % 4] : alpha[y, x, layer];
                original[layer] =
                    baseline == null
                        ? _originalControls![layer / 4][(rect.y + y) * Tile.Width + rect.x + x][layer % 4]
                        : baseline[y, x, layer];
            }
            MapTerrainPainting.Blend(current, original, stroke.Layer, weight, stroke.Mode == "RestoreTexture");
            for (var layer = 0; layer < count; layer++)
                if (alpha == null)
                    colors[layer / 4][y * rect.width + x][layer % 4] = current[layer];
                else
                    alpha[y, x, layer] = current[layer];
            _mix!.data[rect.x + x, rect.y + y] = (byte)MapTerrainPainting.Dominant(current);
        }
        if (alpha != null)
            _data.SetAlphamaps(rect.x, rect.y, alpha);
        for (var i = 0; i < colors.Length; i++)
        {
            _controls![i].SetPixels(rect.x, rect.y, rect.width, rect.height, colors[i]);
        }
        _textureDirty = true;
    }

    internal void FlushTextures()
    {
        if (!_textureDirty)
            return;
        if (_controls != null)
            foreach (var control in _controls)
                control.Apply(true, false);
        _data.SetBaseMapDirty();
        _textureDirty = false;
    }

    internal async UniTask EnsureGrass()
    {
        if (Tile.GrassError.Length > 0)
            throw new InvalidOperationException(Tile.GrassError);
        foreach (var manager in Tile.Managers)
        {
            if (_grass.ContainsKey(manager))
                continue;
            await WaitManager(manager);
            var maps = TerrainGrassMaps.Capture(manager);
            if (maps.Count != Tile.Target.Grass)
                throw new InvalidOperationException("Native grass palette changed.");
            var originals = new List<int[,]>();
            var working = new List<float[,]>();
            foreach (var map in maps)
            {
                if (map == null || map.GetLength(0) == 0 || map.GetLength(1) == 0)
                    throw new InvalidOperationException("Native grass density map is missing.");
                originals.Add(map);
                working.Add(Floats(map));
            }
            _grassOriginal.Add(manager, originals);
            _grass.Add(manager, working);
        }
        if (Tile.Managers.Length == 0 && _unityGrass.Count == 0)
            for (var layer = 0; layer < Tile.Target.Grass; layer++)
            {
                var map = _original.GetDetailLayer(0, 0, _original.detailWidth, _original.detailHeight, layer);
                _unityGrassOriginal.Add(layer, map);
                _unityGrass.Add(layer, Floats(map));
            }
    }

    internal void StampGrass(MapTerrainStroke stroke, SpatialVector point)
    {
        foreach (var pair in _grass)
            for (var layer = 0; layer < pair.Value.Count; layer++)
                if (stroke.Mode is "ClearGrass" or "RestoreGrass" || layer == stroke.Layer)
                    PaintDensity(pair.Value[layer], _grassOriginal[pair.Key][layer], stroke, point);
        foreach (var pair in _unityGrass)
            if (stroke.Mode is "ClearGrass" or "RestoreGrass" || pair.Key == stroke.Layer)
                PaintDensity(pair.Value, _unityGrassOriginal[pair.Key], stroke, point);
        _grassChanged = true;
    }

    private void PaintDensity(float[,] map, int[,] original, MapTerrainStroke stroke, SpatialVector point)
    {
        var width = map.GetLength(1);
        var height = map.GetLength(0);
        var rect = Region(point, stroke.Radius, width, height);
        for (var y = rect.y; y < rect.yMax; y++)
        for (var x = rect.x; x < rect.xMax; x++)
            map[y, x] = MapTerrainPainting.GrassDensity(
                map[y, x],
                original[y, x],
                MapTerrainPainting.Weight((x + .5f) * Tile.Target.Width / width, (y + .5f) * Tile.Target.Depth / height, point, stroke),
                stroke
            );
    }

    internal async UniTask FlushGrass(bool restore = false)
    {
        if (!_grassChanged && !restore)
            return;
        foreach (var pair in _grass)
        {
            if (!pair.Key || !pair.Key.terrain)
                continue;
            var maps = restore ? _grassOriginal[pair.Key] : new List<int[,]>();
            if (!restore)
                foreach (var map in pair.Value)
                    maps.Add(Integers(map));
            await WaitManager(pair.Key);
            // SetDetailMapData alone only sets threadDetailMapData. Recreate spatial cells as well.
            pair.Key.SetDetailMapData(maps);
            Initializing.SetValue(pair.Key, true);
            pair.Key.InitializeSpatialPartitioning();
            await WaitManager(pair.Key);
        }
        if (!restore)
            foreach (var pair in _unityGrass)
                _data.SetDetailLayer(0, 0, pair.Key, Integers(pair.Value));
        _grassChanged = false;
    }

    internal static async UniTask WaitManager(GPUInstancerDetailManager manager)
    {
        var deadline = Time.realtimeSinceStartup + 30;
        while (
            manager
            && (
                (bool)Initializing.GetValue(manager)
                || (bool)Replacing.GetValue(manager)
                || manager.activeThreads.Count > 0
                || manager.threadStartQueue.Count > 0
            )
        )
        {
            if (manager.threadException != null)
                throw new InvalidOperationException("Native grass rebuild failed.", manager.threadException);
            if (Time.realtimeSinceStartup >= deadline)
                throw new TimeoutException("Native grass rebuild did not finish within 30 seconds.");
            await UniTask.NextFrame();
        }
    }

    internal async UniTask Restore()
    {
        if (_disposed)
            return;
        _disposed = true;
        try
        {
            await FlushGrass(true);
        }
        finally
        {
            if (Tile.Surface && Tile.Surface.terrainData == _data)
                Tile.Surface.terrainData = _original;
            if (_collider && _collider!.terrainData == _data)
                _collider.terrainData = _collisionData!;
            if (Tile.Ballistic)
            {
                if (Tile.Ballistic!.TerrainData == _data)
                    Tile.Ballistic.TerrainData = _ballisticData!;
                if (_mix != null && ReferenceEquals(MixField.GetValue(Tile.Ballistic), _mix))
                    MixField.SetValue(Tile.Ballistic, _originalMix);
            }
            if (Tile.Splat)
            {
                if (_controls != null)
                    TerrainTile.SetControls(Tile.Splat!, Tile.Controls);
                Tile.Splat!.matInstance = _splatMaterial!;
            }
            if (Tile.Surface && _ownedMaterial && Tile.Surface.materialTemplate == _ownedMaterial)
                Tile.Surface.materialTemplate = _material!;
            try
            {
                if (Tile.Splat && _texturesChanged)
                    Tile.Splat!.Sync();
            }
            finally
            {
                TerrainPaintVisibility.Release(Tile.Surface);
                if (_controls != null)
                    foreach (var texture in _controls)
                        if (texture)
                            UnityEngine.Object.Destroy(texture);
                if (_ownedMaterial)
                    UnityEngine.Object.Destroy(_ownedMaterial);
                UnityEngine.Object.Destroy(_data);
            }
        }
    }

    private RectInt Region(SpatialVector p, float radius, int width, int height)
    {
        var region = MapTerrainPainting.Region(p, radius, Tile.Target.Width, Tile.Target.Depth, width, height);
        return new(region.X, region.Y, region.Width, region.Height);
    }

    private static float[,] Floats(int[,] map)
    {
        var result = new float[map.GetLength(0), map.GetLength(1)];
        for (var y = 0; y < map.GetLength(0); y++)
        for (var x = 0; x < map.GetLength(1); x++)
            result[y, x] = map[y, x];
        return result;
    }

    private static int[,] Integers(float[,] map)
    {
        var result = new int[map.GetLength(0), map.GetLength(1)];
        for (var y = 0; y < map.GetLength(0); y++)
        for (var x = 0; x < map.GetLength(1); x++)
            result[y, x] = (int)Math.Round(map[y, x]);
        return result;
    }

    internal static Texture2D ReadableCopy(Texture source)
    {
        var previous = RenderTexture.active;
        var srgb = GL.sRGBWrite;
        var temporary = RenderTexture.GetTemporary(
            source.width,
            source.height,
            0,
            RenderTextureFormat.ARGB32,
            RenderTextureReadWrite.Linear
        );
        Texture2D? copy = null;
        try
        {
            GL.sRGBWrite = false;
            Graphics.Blit(source, temporary);
            RenderTexture.active = temporary;
            copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, true, true)
            {
                name = source.name,
                wrapMode = source.wrapMode,
                filterMode = source.filterMode,
            };
            copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            copy.Apply(true, false);
            return copy;
        }
        catch
        {
            if (copy)
                UnityEngine.Object.Destroy(copy);
            throw;
        }
        finally
        {
            GL.sRGBWrite = srgb;
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(temporary);
        }
    }
}
