using System.Globalization;
using UnityEngine;
using WTT.Campaigns.Client.Authoring.Console;
using WTT.Campaigns.Client.Authoring.Scenes;
using WTT.Campaigns.Client.Authoring.TerrainEditing;
using WTT.Campaigns.Client.Authoring.Views;
using WTT.Campaigns.Shared.Spatial;
using WTT.Campaigns.UI.Controls;

namespace WTT.Campaigns.Client.Authoring.Editor;

public sealed partial class RaidEditor
{
    private TerrainTile? _terrainTile,
        _terrainStrokeTile;
    private MapTerrainStroke? _terrainStroke;
    private RaidEditorSession? _terrainSession;
    private long _terrainContent;
    private string _terrainLayout = "",
        _terrainBrush = "",
        _terrainFeedback = "";
    private bool _terrainGrass;
    private int _terrainLayer,
        _terrainDensity = 8;
    private float _terrainRadius = 2,
        _terrainStrength = .5f,
        _terrainFalloff = .5f,
        _terrainRefresh;
    private Vector3? _terrainCursor,
        _terrainLastStamp;
    private readonly Vector3[] _terrainRing = new Vector3[49];
    private bool TerrainBusy => _mapScene?.Terrain.Busy == true;
    private bool CanPaintTerrain =>
        EditorMode.Ready
        && _open
        && !_walking
        && !AiPreviewBusy
        && !IsDragging
        && !Catalog.Placing
        && _walkAssetLifetime == null
        && Layout != null
        && !TerrainBusy
        && _session is { Definition: not null, Retired: false, Previewing: false, Conflict: null }
        && (!_session.Busy || _terrainStroke != null);

    private void BindTerrainPanel(RaidEditorView view)
    {
        view.Button("TerrainTextures", () => TerrainCategory(false));
        view.Button("TerrainGrass", () => TerrainCategory(true));
        foreach (
            var (id, mode) in new[]
            {
                ("TerrainPaint", "Texture"),
                ("TerrainRestoreTexture", "RestoreTexture"),
                ("TerrainAddGrass", "AddGrass"),
                ("TerrainRemoveGrass", "RemoveGrass"),
                ("TerrainClearGrass", "ClearGrass"),
                ("TerrainRestoreGrass", "RestoreGrass"),
            }
        )
            view.Button(
                id,
                () =>
                    TerrainAction(() =>
                    {
                        CancelTerrainStroke();
                        if (!CanPaintTerrain)
                            throw new InvalidOperationException("Choose a layout and wait for synchronization or preview work to finish.");
                        _navigationBrush = "";
                        _navigationStroke = null;
                        Catalog.CancelPlacement();
                        _terrainBrush = mode;
                        view.ReleaseFocus();
                        _terrainFeedback = "Drag on the selected tile. Escape cancels the stroke.";
                    })
            );
        view.Button("TerrainOff", StopTerrainBrush);
        view.Button(
            "TerrainClearEdits",
            () =>
                TerrainAction(() =>
                {
                    StopTerrainBrush();
                    if (!CanPaintTerrain)
                        throw new InvalidOperationException("Wait for terrain or synchronization work to finish.");
                    var id = Layout!.Id;
                    _session!.Edit(d => d.MapLayouts.Find(l => l.Id == id)!.Terrain = null);
                    _terrainFeedback = "Layout paint removed; Ctrl+Z restores it.";
                })
        );
        void Number(string id, float minimum, float maximum, Action<float> set) =>
            view.Input(
                id,
                text =>
                    TerrainAction(() =>
                    {
                        if (
                            !float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                            || float.IsNaN(value)
                            || value < minimum
                            || value > maximum
                        )
                            throw new ArgumentException($"Enter a value between {minimum} and {maximum}.");
                        set(value);
                    })
            );
        Number("TerrainRadius", .5f, 20, value => _terrainRadius = value);
        Number("TerrainStrength", 1, 100, value => _terrainStrength = value / 100);
        Number("TerrainFalloff", 0, 100, value => _terrainFalloff = value / 100);
        Number("TerrainDensity", 0, MapTerrainPainting.MaxDensity, value => _terrainDensity = (int)Math.Round(value));
    }

    private void TerrainCategory(bool grass)
    {
        StopTerrainBrush();
        _terrainGrass = grass;
        _terrainLayer = 0;
        RefreshTerrainPalette();
    }

    private void RefreshTerrainPalette() =>
        _view?.TerrainPalette(
            _terrainTile,
            _terrainGrass,
            _terrainLayer,
            index =>
            {
                CancelTerrainStroke();
                _terrainLayer = index;
                _terrainBrush = _terrainGrass ? "AddGrass" : "Texture";
                _navigationBrush = "";
                _navigationStroke = null;
                _view.ReleaseFocus();
            }
        );

    private void TerrainAction(Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            CancelTerrainStroke();
            _terrainFeedback = e.Message;
            ReportFeedback(e.Message, ConsoleSeverity.Error);
        }
        _terrainRefresh = 0;
    }

    private void StopTerrainBrush()
    {
        CancelTerrainStroke();
        _terrainBrush = "";
        _terrainCursor = null;
    }

    private void CancelTerrainStroke()
    {
        if (_terrainStroke == null)
            return;
        _terrainStroke = null;
        _terrainStrokeTile = null;
        _terrainLastStamp = null;
        _mapScene?.Terrain.EndPreview(Layout?.Terrain, false);
        _terrainFeedback = "Stroke cancelled; restoring saved terrain.";
    }

    private void SettleTerrainStroke()
    {
        if (_terrainStroke == null)
            return;
        if (
            !CanPaintTerrain
            || _terrainSession != _session
            || _terrainContent != _session?.ContentVersion
            || _terrainLayout != Layout?.Id
            || _view?.Windows.IsOpen("Terrain") != true
        )
        {
            CancelTerrainStroke();
            return;
        }
        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButton(0))
            return;
        TerrainAction(() =>
        {
            var recipes = RaidEditorSession.Copy(Layout!.Terrain ?? new List<MapTerrainRecipe>());
            var tile = _terrainStrokeTile!;
            var stroke = _terrainStroke!;
            var recipe = recipes.Find(r => r.Target.Scene == tile.Target.Scene && r.Target.Path == tile.Target.Path);
            if (recipe != null && recipe.Target.Fingerprint != tile.Target.Fingerprint)
                throw new InvalidOperationException("The saved terrain binding no longer matches this tile.");
            if (recipe == null)
            {
                recipe = new() { Target = RaidEditorSession.Copy(tile.Target) };
                recipes.Add(recipe);
            }
            recipe.Strokes.Add(stroke);
            _session!.Edit(d => d.MapLayouts.Find(l => l.Id == _terrainLayout)!.Terrain = recipes);
            _mapScene!.Terrain.EndPreview(recipes, MapTerrainPainting.IsTexture(stroke.Mode), cancel: false);
            _terrainStroke = null;
            _terrainStrokeTile = null;
            _terrainLastStamp = null;
            _terrainFeedback = "Stroke saved · Ctrl+Z to undo.";
        });
    }

    private void RefreshTerrainPanel()
    {
        if (!_open || _view?.Valid != true)
            return;
        if (!_view.Windows.IsOpen("Terrain"))
        {
            StopTerrainBrush();
            return;
        }
        if (Time.unscaledTime < _terrainRefresh)
            return;
        _terrainRefresh = Time.unscaledTime + .25f;
        if (_terrainTile != null && !_terrainTile.Surface)
        {
            StopTerrainBrush();
            _terrainTile = null;
            RefreshTerrainPalette();
        }
        if (_terrainStroke == null && _terrainLayout != Layout?.Id)
        {
            _terrainLayout = Layout?.Id ?? "";
            _terrainTile = null;
            _terrainLayer = 0;
            StopTerrainBrush();
            RefreshTerrainPalette();
        }
        _view.Text(
            "TerrainState",
            Layout == null ? "Choose a layout first."
                : _terrainTile == null ? "Point at exposed terrain to browse its palette."
                : Layout.Name + " · " + _terrainTile.Surface.name
        );
        _view.Highlight("TerrainTextures", !_terrainGrass);
        _view.Highlight("TerrainGrass", _terrainGrass);
        _view.Visible("TerrainDensity", _terrainGrass);
        _view.Visible("TerrainTextureActions", !_terrainGrass);
        _view.Visible("TerrainTextureRestoreActions", !_terrainGrass);
        _view.Visible("TerrainGrassActions", _terrainGrass);
        _view.Visible("TerrainGrassRestoreActions", _terrainGrass);
        _view.Element("TerrainPalette").SetEnabled(CanPaintTerrain && _terrainStroke == null);
        var issue =
            _terrainTile == null ? ""
            : _terrainGrass ? _terrainTile.GrassError
            : _terrainTile.TextureError;
        _view.Text(
            "TerrainFeedback",
            TerrainBusy ? _mapScene!.Terrain.Status
                : _mapScene?.Terrain.Error is { Length: > 0 } error ? error
                : issue.Length > 0 ? issue
                : _terrainFeedback
        );
    }

    private bool TerrainPaintInput()
    {
        _terrainCursor = null;
        if (_view?.Windows.IsOpen("Terrain") != true || !_camera || !CanPaintTerrain)
            return _terrainBrush.Length > 0 || TerrainBusy;
        if (_view.PointerOver || _view.Typing || CameraLooking || _view.Windows.HasMenu)
        {
            _terrainLastStamp = null;
            return _terrainBrush.Length > 0;
        }
        if (
            !Physics.Raycast(_camera!.EditorScreenPointToRay(Input.mousePosition), out var hit, 2000, ~0, QueryTriggerInteraction.Ignore)
            || hit.collider is not TerrainCollider collider
        )
        {
            _terrainLastStamp = null;
            return _terrainBrush.Length > 0;
        }
        var surface = collider.GetComponent<UnityEngine.Terrain>();
        if (!surface)
            return _terrainBrush.Length > 0;
        _mapScene ??= new();
        if (_terrainStroke == null && _terrainTile?.Surface != surface)
        {
            TerrainAction(() =>
            {
                _terrainTile = _mapScene.Terrain.Inspect(surface);
                _terrainLayer = 0;
                RefreshTerrainPalette();
            });
        }
        if (_terrainBrush.Length == 0)
            return false;
        var tile = _terrainStrokeTile ?? _terrainTile;
        if (tile == null || tile.Surface != surface)
        {
            _terrainLastStamp = null;
            return true;
        }
        _terrainCursor = hit.point;
        if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
            return true;
        if (Input.GetMouseButtonDown(0))
            TerrainAction(() =>
            {
                tile = _mapScene.Terrain.Inspect(surface);
                var error = MapTerrainPainting.IsTexture(_terrainBrush) ? tile.TextureError : tile.GrassError;
                if (error.Length > 0)
                    throw new InvalidOperationException(error);
                _mapScene.Terrain.BeginPreview(tile);
                _terrainStrokeTile = tile;
                _terrainStroke = new()
                {
                    Mode = _terrainBrush,
                    Layer = _terrainLayer,
                    Radius = _terrainRadius,
                    Strength = _terrainStrength,
                    Falloff = _terrainFalloff,
                    Density = _terrainDensity,
                };
                _terrainSession = _session;
                _terrainContent = _session!.ContentVersion;
                _terrainLayout = Layout!.Id;
                _session.Hold = true;
                _terrainLastStamp = null;
            });
        if (_terrainStroke != null && Input.GetMouseButton(0))
            TerrainAction(() =>
            {
                var local = hit.point - surface.transform.position;
                local.y = 0;
                var step = Math.Max(.1f, _terrainStroke.Radius * .25f);
                var count = 0;
                if (_terrainLastStamp == null)
                    Stamp(local);
                while (_terrainLastStamp is { } previous && (local - previous).magnitude >= step)
                {
                    if (++count > 128)
                        throw new InvalidOperationException("Brush movement was too large; the stroke was cancelled.");
                    Stamp(previous + (local - previous).normalized * step);
                }
                _mapScene.Terrain.FlushPreview(tile);
                void Stamp(Vector3 position)
                {
                    if (_terrainStroke.Points.Count >= 4096)
                        throw new InvalidOperationException("End this stroke before painting more (4096 stamps per stroke).");
                    var point = new SpatialVector
                    {
                        X = Math.Clamp(position.x, 0, tile.Target.Width),
                        Z = Math.Clamp(position.z, 0, tile.Target.Depth),
                    };
                    _terrainStroke.Points.Add(point);
                    _mapScene.Terrain.PreviewStamp(tile, _terrainStroke, point);
                    _terrainLastStamp = position;
                }
            });
        return true;
    }

    private void DrawTerrainBrush()
    {
        var tile = _terrainStrokeTile ?? _terrainTile;
        if (_terrainCursor is not { } point || tile == null || !tile.Surface || _terrainBrush.Length == 0)
            return;
        var radius = _terrainStroke?.Radius ?? _terrainRadius;
        var origin = tile.Surface.transform.position;
        for (var i = 0; i < _terrainRing.Length; i++)
        {
            var p = point + new Vector3(Mathf.Cos(i * Mathf.PI / 24), 0, Mathf.Sin(i * Mathf.PI / 24)) * radius;
            p.x = Mathf.Clamp(p.x, origin.x, origin.x + tile.Target.Width);
            p.z = Mathf.Clamp(p.z, origin.z, origin.z + tile.Target.Depth);
            p.y = origin.y + tile.Surface.SampleHeight(p) + .05f;
            _terrainRing[i] = p;
        }
        Line(_terrainRing, _terrainGrass ? Color.green : Color.yellow);
    }
}
