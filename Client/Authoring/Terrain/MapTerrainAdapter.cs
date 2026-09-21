using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using WTT.Campaigns.Client.Authoring.Scenes;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Client.Authoring.TerrainEditing;

internal sealed class MapTerrainAdapter : IDisposable
{
    // A disposed scene can still be restoring native grass. New scene owners wait for that restoration.
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly Dictionary<string, TerrainTileState> _tiles = new();
    private readonly Dictionary<string, List<MapTerrainStroke>> _applied = new();
    private List<MapTerrainRecipe> _requested = new();
    private string _key = "[]";
    private int _revision;
    private bool _disposed,
        _preview;
    internal bool Busy { get; private set; }
    internal string Error { get; private set; } = "";
    internal string Status { get; private set; } = "";
    internal bool Previewing => _preview;

    private static string Key(MapTerrainTarget target) => target.Scene + "|" + target.Path;

    internal TerrainTile Inspect(UnityEngine.Terrain terrain)
    {
        foreach (var state in _tiles.Values)
            if (state.Tile.Surface == terrain)
                return state.Tile;
        if (Busy || Gate.CurrentCount == 0 && !_preview)
            throw new InvalidOperationException("Wait for terrain changes to finish.");
        return TerrainTile.Inspect(terrain);
    }

    internal void BeginPreview(TerrainTile tile)
    {
        if (_disposed || Busy || _preview || !Gate.Wait(0))
            throw new InvalidOperationException("Wait for terrain changes to finish.");
        _preview = true;
        try
        {
            if (!_tiles.ContainsKey(Key(tile.Target)))
                _tiles.Add(Key(tile.Target), new(tile));
        }
        catch
        {
            _preview = false;
            Gate.Release();
            throw;
        }
    }

    internal void PreviewStamp(TerrainTile tile, MapTerrainStroke stroke, SpatialVector point)
    {
        if (!_preview || Busy)
            throw new InvalidOperationException("Terrain preview is unavailable.");
        if (MapTerrainPainting.IsTexture(stroke.Mode))
            _tiles[Key(tile.Target)].StampTexture(stroke, point);
    }

    internal void FlushPreview(TerrainTile tile) => _tiles[Key(tile.Target)].FlushTextures();

    internal void EndPreview(List<MapTerrainRecipe>? recipes, bool acceptTexture, bool cancel = true)
    {
        if (!_preview)
            return;
        _preview = false;
        Gate.Release();
        if (acceptTexture)
        {
            _requested = RaidEditorSession.Copy(recipes ?? new());
            _key = JsonConvert.SerializeObject(_requested);
            Remember(_requested);
            Error = "";
            Status = "Terrain paint saved.";
        }
        else
        {
            // A cancelled texture preview is outside the committed recipe; restore before replay.
            if (cancel)
            {
                _applied.Clear();
                _key = "";
            }
            Reconcile(recipes);
        }
    }

    internal void Reconcile(List<MapTerrainRecipe>? recipes)
    {
        if (_disposed || _preview)
            return;
        var key = JsonConvert.SerializeObject(recipes ?? new());
        if (key == _key)
            return;
        _key = key;
        _requested = RaidEditorSession.Copy(recipes ?? new());
        _revision++;
        if (!Busy)
            Run().Forget(e => Plugin.Error(e));
    }

    private async UniTask Run()
    {
        Busy = true;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!Gate.Wait(0))
            await UniTask.NextFrame();
        try
        {
            do
            {
                var revision = _revision;
                Error = "";
                Status = _disposed ? "Restoring original terrain…" : "Applying terrain paint…";
                try
                {
                    if (_disposed)
                    {
                        await Restore();
                        return;
                    }
                    var recipes = _requested;
                    var errors = MapTerrainPainting.Errors(recipes);
                    if (errors.Count > 0)
                        throw new InvalidOperationException(string.Join("\n", errors));
                    var grouped = Group(recipes);
                    var append = _tiles.Count == _applied.Count;
                    foreach (var pair in _applied)
                    {
                        if (!grouped.TryGetValue(pair.Key, out var next) || next.Count < pair.Value.Count)
                        {
                            append = false;
                            break;
                        }
                        for (var i = 0; i < pair.Value.Count; i++)
                            if (JsonConvert.SerializeObject(pair.Value[i]) != JsonConvert.SerializeObject(next[i]))
                            {
                                append = false;
                                break;
                            }
                    }
                    if (!append)
                        await Restore();
                    foreach (var recipe in recipes)
                    {
                        var key = Key(recipe.Target);
                        if (_tiles.TryGetValue(key, out var existing))
                        {
                            if (existing.Tile.Target.Fingerprint != recipe.Target.Fingerprint)
                                throw new InvalidOperationException("Terrain binding changed: " + recipe.Target.Path);
                            continue;
                        }
                        var terrain = Resolve(recipe.Target);
                        var tile = TerrainTile.Inspect(terrain);
                        if (JsonConvert.SerializeObject(tile.Target) != JsonConvert.SerializeObject(recipe.Target))
                            throw new InvalidOperationException("Terrain assets changed; rebind or remove paint for " + recipe.Target.Path);
                        _tiles.Add(key, new(tile));
                    }
                    foreach (var pair in grouped)
                    {
                        var state = _tiles[pair.Key];
                        var start = _applied.TryGetValue(pair.Key, out var previous) ? previous.Count : 0;
                        for (var index = start; index < pair.Value.Count; index++)
                        {
                            var stroke = pair.Value[index];
                            var texture = MapTerrainPainting.IsTexture(stroke.Mode);
                            if (!texture)
                            {
                                Status = "Preparing native grass density…";
                                await state.EnsureGrass();
                            }
                            var stamps = 0;
                            foreach (var point in stroke.Points)
                            {
                                if (_disposed || revision != _revision)
                                    break;
                                if (texture)
                                    state.StampTexture(stroke, point);
                                else
                                    state.StampGrass(stroke, point);
                                if (++stamps % 8 == 0)
                                {
                                    state.FlushTextures();
                                    await UniTask.NextFrame();
                                }
                            }
                            if (_disposed || revision != _revision)
                                break;
                        }
                        state.FlushTextures();
                        Status = "Rebuilding grass for the main view and scopes…";
                        await state.FlushGrass();
                        if (_disposed || revision != _revision)
                            break;
                    }
                    if (_disposed || revision != _revision)
                    {
                        await Restore();
                        continue;
                    }
                    Remember(recipes);
                    Status = "Terrain paint applied.";
                }
                catch (Exception e)
                {
                    Error = "Terrain paint could not be applied: " + e.Message;
                    Status = Error;
                    Plugin.Error(e);
                    await Restore();
                }
                if (!_disposed && revision == _revision)
                    break;
            } while (true);
        }
        finally
        {
            Gate.Release();
            Busy = false;
            Plugin.LogInfo(
                $"Terrain paint: {timer.ElapsedMilliseconds} ms, {_tiles.Count} owned tiles, {(_disposed ? "restored" : Error.Length == 0 ? "applied" : "failed")}. Managed memory: {GC.GetTotalMemory(false) / 1048576d:0.0} MiB."
            );
        }
    }

    private static Dictionary<string, List<MapTerrainStroke>> Group(List<MapTerrainRecipe> recipes)
    {
        var grouped = new Dictionary<string, List<MapTerrainStroke>>();
        foreach (var recipe in recipes)
        {
            var key = Key(recipe.Target);
            if (!grouped.TryGetValue(key, out var strokes))
                grouped[key] = strokes = new();
            strokes.AddRange(recipe.Strokes);
        }
        return grouped;
    }

    private void Remember(List<MapTerrainRecipe> recipes)
    {
        _applied.Clear();
        foreach (var pair in Group(RaidEditorSession.Copy(recipes)))
            _applied.Add(pair.Key, pair.Value);
    }

    private async UniTask Restore()
    {
        foreach (var state in _tiles.Values)
            try
            {
                await state.Restore();
            }
            catch (Exception e)
            {
                Error = "Terrain restoration needs attention: " + e.Message;
                Plugin.Error(e);
            }
        _tiles.Clear();
        _applied.Clear();
    }

    private static UnityEngine.Terrain Resolve(MapTerrainTarget target)
    {
        UnityEngine.Terrain? result = null;
        foreach (var terrain in Resources.FindObjectsOfTypeAll<UnityEngine.Terrain>())
        {
            if (
                !terrain.gameObject.scene.IsValid()
                || !terrain.gameObject.scene.isLoaded
                || terrain.gameObject.scene.name != target.Scene
                || MapSceneAdapter.PathOf(terrain.transform) != target.Path
            )
                continue;
            if (result)
                throw new InvalidOperationException("Ambiguous terrain: " + target.Path);
            result = terrain;
        }
        return result ? result! : throw new InvalidOperationException("Terrain is not loaded: " + target.Path);
    }

    internal void RefreshVisibility()
    {
        foreach (var state in _tiles.Values)
            TerrainPaintVisibility.Refresh(state.Tile.Surface);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _revision++;
        if (_preview)
        {
            _preview = false;
            Gate.Release();
        }
        if (!Busy && _tiles.Count > 0)
            Run().Forget(e => Plugin.Error(e));
    }
}
