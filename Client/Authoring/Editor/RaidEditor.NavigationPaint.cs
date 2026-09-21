using System.Globalization;
using Cysharp.Threading.Tasks;
using EFT;
using UnityEngine;
using UnityEngine.AI;
using WTT.Campaigns.Client.Authoring.Navigation;
using WTT.Campaigns.Client.Authoring.Scenes;
using WTT.Campaigns.Client.Authoring.Views;
using WTT.Campaigns.Client.Encounters;
using WTT.Campaigns.Client.Spatial;
using WTT.Campaigns.Shared.Spatial;
using WTT.Campaigns.UI.Controls;
using ZLinq;

namespace WTT.Campaigns.Client.Authoring.Editor;

public sealed partial class RaidEditor
{
    private NavigationPaintPreview? _navigationPaint;
    private NavigationPaintPreview? _aiPaintObservation;
    private string _navigationBrush = "";
    private float _navigationBrushRadius = 2;
    private Vector3? _navigationCursor,
        _navigationLinkStart,
        _navigationPathStart;
    private float? _navigationFloor;
    private float? _navigationDisplayFloor;
    private Vector3? _navigationLastStamp;
    private bool _navigationFloorLock,
        _navigationPaintHidden;
    private MapNavigationRecipe? _navigationStroke;
    private NavigationBuildStamp _navigationStrokeStamp;
    private int _navigationPaintRevision;
    private long _navigationPaintRendered = -1;
    private string _navigationPaintLayout = "";
    private NavigationSurfaceRenderer? _navigationAddOverlay,
        _navigationBlockOverlay,
        _navigationNativeOverlay;

    private readonly Dictionary<(int X, int Z, float Y), (Vector3[] Vertices, int[] Indices)> _navigationContours = new();
    private long _navigationContourGeometry = -1;
    private int _navigationContourIncomplete,
        _navigationContourPending;
    private readonly Vector3[] _navigationBrushRing = new Vector3[49];
    private readonly bool[] _navigationBrushRingValid = new bool[49];
    private Vector3? _navigationBrushRingOrigin;
    private float _navigationBrushRingRadius;
    private long _navigationBrushRingGeometry = -1;
    private readonly RaycastHit[] _navigationContourHits = new RaycastHit[64];

    private bool CanPaintNavigation => CanEditNavigationRecipe && _navigationPaint?.Busy != true && _navigationPaint?.Active != true;

    private void SettleNavigationStroke()
    {
        if (_navigationStroke == null)
            return;
        if (!_open || _view?.Windows.IsOpen("Navigation") != true || !_navigationStrokeStamp.Equals(NavigationStamp()))
        {
            _navigationStroke = null;
            _navigationPaintRevision++;
            _navigationPaintRevision++;
            return;
        }
        if (Input.GetMouseButton(0))
            return;
        var recipe = _navigationStroke;
        _navigationStroke = null;
        _navigationPaintRevision++;
        NavigationPanelAction(() => EditNavigationRecipe(_ => recipe));
    }

    private void BindNavigationPainting(RaidEditorView view)
    {
        foreach (var mode in new[] { "Add", "Block", "Erase", "Connect", "Path", "Off" })
        {
            var tool = mode;
            view.Button(
                "NavPaint" + mode,
                () =>
                    NavigationPanelAction(() =>
                    {
                        if (tool is not ("Off" or "Path") && !CanPaintNavigation)
                            throw new InvalidOperationException("Clear the preview before editing navigation paint.");
                        _navigationStroke = null;
                        _navigationBrush = tool == "Off" ? "" : tool;
                        _navigationLinkStart = _navigationPathStart = null;
                        _navigationPaintHidden = false;
                        _view?.ShowNavigationOverlay();
                        _view?.ReleaseFocus();
                    })
            );
        }
        view.Input(
            "NavBrushSize",
            text =>
                NavigationPanelAction(() =>
                {
                    if (
                        !float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var radius)
                        || float.IsNaN(radius)
                        || radius < .75f
                        || radius > 8
                    )
                        throw new ArgumentException("Brush radius must be between 0.75 and 8 metres.");
                    _navigationBrushRadius = radius;
                })
        );
        view.Button(
            "NavFloorLock",
            () =>
            {
                _navigationFloorLock = view.IsChecked("NavFloorLock");
                _navigationFloor = null;
            }
        );
        view.Button(
            "NavClearConnections",
            () =>
                NavigationPanelAction(() =>
                {
                    if (!CanPaintNavigation)
                        throw new InvalidOperationException("Clear the preview before editing connections.");
                    EditNavigationRecipe(recipe =>
                    {
                        if (recipe != null)
                            recipe.Connections.Clear();
                        return recipe;
                    });
                })
        );
        view.Button("NavAdvanced", RefreshNavigationAdvanced);
        view.Button("NavProjectOntoGround", () => NavigationSurfaceRenderer.ProjectOntoGround = view.IsChecked("NavProjectOntoGround"));
    }

    private void RefreshNavigationAdvanced()
    {
        if (_view?.Valid != true)
            return;
        var show = _view.IsChecked("NavAdvanced");
        _view.Visible("NavDiagnosticsCard", show);
    }

    private void BuildNavigationPaint()
    {
        if (!CanPaintNavigation || _navigationStroke != null)
            throw new InvalidOperationException("End the brush stroke, wait for synchronization, and clear any active preview first.");
        if (Layout?.Navigation is not { } recipe || recipe.Cells.Count == 0)
            throw new InvalidOperationException("Paint an Add or Block area first. Building never fills unpainted areas.");
        _navigationBrush = "";
        _navigationPaintHidden = false;
        _view?.ShowNavigationOverlay();
        var session = _session!;
        _navigationHealthInvalid = true;
        var content = session.ContentVersion;
        var layout = RaidEditorSession.Copy(Layout);
        _navigationPaint ??= new(NavigationStamp, LogNavigationFeedback);
        _navigationPaint.DisplayFloor(_navigationDisplayFloor);
        _navigationPaint.Build(
            RaidEditorSession.Copy(recipe),
            async token =>
            {
                _mapScene ??= new();
                await _mapScene.ApplyAsync(layout, false, token);
                if (_session != session || session.ContentVersion != content || _layoutId != layout.Id)
                    throw new OperationCanceledException("Layout changed during scenery preparation.");
            }
        );
    }

    private bool NavigationPaintInput()
    {
        if (_navigationBrush.Length == 0)
            return false;
        if (_view?.Windows.IsOpen("Navigation") != true || !NavigationAvailable())
        {
            _navigationStroke = null;
            _navigationCursor = null;
            _navigationBrush = "";
            _navigationPaintRevision++;
            return false;
        }
        if (_navigationStroke != null && !_navigationStrokeStamp.Equals(NavigationStamp()))
        {
            _navigationStroke = null;
            _navigationFeedback = "Stroke discarded because the layout changed.";
        }
        if (Input.GetMouseButtonUp(0) && _navigationStroke != null)
        {
            var recipe = _navigationStroke;
            _navigationStroke = null;
            NavigationPanelAction(() => EditNavigationRecipe(_ => recipe));
        }
        _navigationCursor = null;
        if (_view.PointerOver || _view.Typing || _view.Windows.HasMenu || CameraLooking || !_camera)
            return true;
        var ray = _camera!.EditorScreenPointToRay(Input.mousePosition);
        var hit = _navigationBrush == "Erase" ? ErasePaintHit(ray, out var point) : PaintHit(ray, 1000, out point);
        if (!hit)
            return true;
        if (
            _navigationFloorLock
            && _navigationFloor.HasValue
            && Math.Abs(point.y - _navigationFloor.Value) > MapNavigationPainting.FloorTolerance
        )
            return true;
        _navigationCursor = point;
        if (_navigationBrush == "Path" && Input.GetMouseButtonDown(0))
        {
            if (!_navigationPathStart.HasValue)
            {
                _navigationPathStart = point;
                _navigationFeedback = "Path start selected. Click the destination.";
            }
            else
            {
                var nav = new EncounterNavigation(() => Layout);
                var from = ZoneRuntime.Vector(_navigationPathStart.Value);
                var to = ZoneRuntime.Vector(point);
                var passed =
                    nav.HasStandingClearance(from)
                    && nav.HasStandingClearance(to)
                    && nav.HasCompletePath(from, to)
                    && nav.HasCompletePath(to, from);
                _navigationFeedback = passed
                    ? "PASS: complete two-way physical path on the active navigation."
                    : "NO PATH: the active navigation does not provide a clear two-way connection.";
                LogNavigationFeedback(_navigationFeedback, !passed);
                _navigationPathStart = null;
            }
            _navigationRefreshAt = 0;
            return true;
        }
        if (!CanPaintNavigation)
            return true;
        if (_navigationBrush == "Connect")
        {
            if (Input.GetMouseButtonDown(0))
                NavigationPanelAction(() =>
                {
                    if (!_navigationLinkStart.HasValue)
                    {
                        _navigationLinkStart = point;
                        _navigationFeedback =
                            "Connection start selected. Click the other surface, within 5 metres. Build verifies support and paths.";
                    }
                    else
                    {
                        var from = _navigationLinkStart.Value;
                        EditNavigationRecipe(recipe =>
                        {
                            recipe ??= new();
                            recipe.Version = 2;
                            recipe.Settings = null;
                            recipe.Connections.Add(new() { Start = ZoneRuntime.Vector(from), End = ZoneRuntime.Vector(point) });
                            return recipe;
                        });
                        _navigationLinkStart = null;
                    }
                });
            return true;
        }
        if (_navigationBrush is not ("Add" or "Block" or "Erase"))
            return true;
        if (Input.GetMouseButtonDown(0))
        {
            _navigationStroke = Layout?.Navigation == null ? new() { Version = 2 } : RaidEditorSession.Copy(Layout.Navigation);
            _navigationStrokeStamp = NavigationStamp();
            _navigationLastStamp = null;
            if (_navigationFloorLock)
                _navigationFloor ??= point.y;
        }
        if (_navigationStroke != null && Input.GetMouseButton(0))
        {
            try
            {
                if (_navigationLastStamp is { } previous && (point - previous).sqrMagnitude < .01f)
                    return true;
                _navigationLastStamp = point;
                var radius = _navigationBrushRadius;
                if (_navigationBrush == "Erase")
                {
                    foreach (
                        var cell in _navigationStroke
                            .Cells.AsValueEnumerable()
                            .Where(c =>
                                Mathf.Abs(c.Y - point.y) <= MapNavigationPainting.FloorTolerance
                                && new Vector2(c.X + .5f - point.x, c.Z + .5f - point.z).sqrMagnitude <= radius * radius
                            )
                            .ToArray()
                    )
                        MapNavigationPainting.Stamp(_navigationStroke, cell.X, cell.Z, cell.Y, "Erase");
                    _navigationPaintRevision++;
                    return true;
                }
                for (var z = Mathf.FloorToInt(point.z - radius); z <= Mathf.FloorToInt(point.z + radius); z++)
                for (var x = Mathf.FloorToInt(point.x - radius); x <= Mathf.FloorToInt(point.x + radius); x++)
                {
                    if (new Vector2(x + .5f - point.x, z + .5f - point.z).sqrMagnitude > radius * radius)
                        continue;
                    if (!PaintHit(new Ray(new Vector3(x + .5f, point.y + .6f, z + .5f), Vector3.down), 1.2f, out var floor))
                        continue;
                    MapNavigationPainting.Stamp(_navigationStroke, x, z, floor.y, _navigationBrush);
                }
                _navigationPaintRevision++;
            }
            catch (Exception error)
            {
                _navigationStroke = null;
                _navigationFeedback = error.Message;
                LogNavigationFeedback(error.Message, true);
            }
        }
        return true;
    }

    private bool ErasePaintHit(Ray ray, out Vector3 point)
    {
        point = default;
        if (Mathf.Abs(ray.direction.y) < .0001f)
            return false;
        var limit = 1000f;
        foreach (var hit in Physics.RaycastAll(ray, limit, Physics.AllLayers, QueryTriggerInteraction.Ignore))
            if (!hit.collider.GetComponentInParent<Player>() && !hit.transform.name.StartsWith("CampaignEditor", StringComparison.Ordinal))
                limit = Mathf.Min(limit, hit.distance + .1f);
        var found = false;
        foreach (var cell in (_navigationStroke ?? Layout?.Navigation)?.Cells ?? new List<MapNavigationCell>())
        {
            var distance = (cell.Y - ray.origin.y) / ray.direction.y;
            if (distance < 0 || distance > limit)
                continue;
            var position = ray.GetPoint(distance);
            if (!MapNavigationPainting.Contains(cell, position.x, position.y, position.z))
                continue;
            point = position;
            limit = distance;
            found = true;
        }
        return found;
    }

    private static bool PaintHit(Ray ray, float distance, out Vector3 point)
    {
        var hits = Physics.RaycastAll(ray, distance, Physics.AllLayers, QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            if (hit.collider.GetComponentInParent<Player>() || hit.transform.name.StartsWith("CampaignEditor", StringComparison.Ordinal))
                continue;
            if (hit.collider.attachedRigidbody && !hit.collider.attachedRigidbody.isKinematic)
                break;
            // A front wall occludes the floor behind it; never paint through walls.
            if (hit.normal.y < .5f || hit.collider.GetComponentInParent<EFT.Interactive.Door>())
                break;
            point = hit.point;
            return true;
        }
        point = default;
        return false;
    }

    private bool PaintContourHit(Vector3 requested, out Vector3 point) => PaintContourHit(requested, out point, out _);

    private bool PaintContourHit(Vector3 requested, out Vector3 point, out Vector3 normal)
    {
        point = default;
        normal = Vector3.up;
        var count = Physics.RaycastNonAlloc(
            requested + Vector3.up * .601f,
            Vector3.down,
            _navigationContourHits,
            1.202f,
            Physics.AllLayers,
            QueryTriggerInteraction.Ignore
        );
        if (count == _navigationContourHits.Length)
            return false;
        var nearest = float.PositiveInfinity;
        RaycastHit closest = default;
        for (var i = 0; i < count; i++)
        {
            var hit = _navigationContourHits[i];
            if (hit.collider.GetComponentInParent<Player>() || hit.transform.name.StartsWith("CampaignEditor", StringComparison.Ordinal))
                continue;
            if (hit.distance >= nearest)
                continue;
            nearest = hit.distance;
            closest = hit;
        }
        if (
            !closest.collider
            || closest.normal.y < .5f
            || closest.collider.GetComponentInParent<EFT.Interactive.Door>()
            || (closest.collider.attachedRigidbody && !closest.collider.attachedRigidbody.isKinematic)
        )
            return false;
        point = closest.point;
        normal = closest.normal;
        return true;
    }

    private void RefreshNavigationPainting()
    {
        if (_view?.Valid != true)
            return;
        var view = _view;
        var recipe = _navigationStroke ?? Layout?.Navigation;
        view.Checked("NavProjectOntoGround", NavigationSurfaceRenderer.ProjectOntoGround);
        view.Text(
            "NavPaintState",
            $"Tool: {(_navigationBrush.Length == 0 ? "Off" : _navigationBrush)} · {recipe?.Cells.AsValueEnumerable().Count(c => c.Mode == "Add") ?? 0} Add / {recipe?.Cells.AsValueEnumerable().Count(c => c.Mode == "Block") ?? 0} Block cells · {recipe?.Connections.Count ?? 0} connections"
                + (_navigationContourPending > 0 ? $"\nFollowing ground contours… {_navigationContourPending} cells remaining." : "")
                + (
                    _navigationContourIncomplete > 0
                        ? $"\n{_navigationContourIncomplete} paint cells have unsupported or out-of-floor portions; those portions are not drawn."
                        : ""
                )
        );
        view.Value("NavBrushSize", _navigationBrushRadius.ToString("0.##", CultureInfo.InvariantCulture));
        foreach (var mode in new[] { "Add", "Block", "Erase", "Connect" })
            view.Element("NavPaint" + mode).SetEnabled(CanPaintNavigation);
        view.Element("NavPaintPath").SetEnabled(NavigationAvailable() && _navigationPaint?.Busy != true);
        view.Element("NavClearConnections").SetEnabled(CanPaintNavigation && recipe?.Connections.Count > 0);
        view.Element("NavBuild").SetEnabled(CanPaintNavigation && recipe?.Cells.Count > 0 && _navigationStroke == null);
        view.Element("NavCancel").SetEnabled(_navigationPaint?.Busy == true);
        view.Element("NavRestore").SetEnabled(NavigationAvailable() && _navigationPaint?.Busy != true && _navigationPaint?.Active == true);
        if (_navigationPaint != null)
        {
            view.Text(
                "NavState",
                _navigationPaint.Busy ? "Building manual preview"
                    : _navigationPaint.Active ? "Manual preview active · native retained"
                    : "Native navigation · paint not applied"
            );
            view.Text("NavStatus", _navigationPaint.Status);
        }
        RefreshNavigationAdvanced();
    }

    private void DrawNavigationPaintHandles()
    {
        if (_navigationPaintHidden || _view?.ViewportState.Shows(WTT.Campaigns.UI.Controls.EditorOverlays.Ai) != true)
            return;
        if (_navigationCursor is { } p && _navigationBrush is "Add" or "Block" or "Erase")
        {
            if (
                !_navigationBrushRingOrigin.HasValue
                || (_navigationBrushRingOrigin.Value - p).sqrMagnitude > .0001f
                || _navigationBrushRingRadius != _navigationBrushRadius
                || _navigationBrushRingGeometry != SceneNavigation.Revision
            )
            {
                _navigationBrushRingOrigin = p;
                _navigationBrushRingRadius = _navigationBrushRadius;
                _navigationBrushRingGeometry = SceneNavigation.Revision;
                PaintContourHit(p, out _, out var normal);
                for (var i = 0; i < _navigationBrushRing.Length; i++)
                {
                    var offset = new Vector3(Mathf.Cos(i * Mathf.PI / 24), 0, Mathf.Sin(i * Mathf.PI / 24)) * _navigationBrushRadius;
                    // The tangent gives a floor reference; the final height is physical support.
                    offset.y = -(normal.x * offset.x + normal.z * offset.z) / Mathf.Max(.5f, normal.y);
                    _navigationBrushRingValid[i] = PaintContourHit(p + offset, out var grounded);
                    _navigationBrushRing[i] = grounded + Vector3.up * .06f;
                }
            }
            var color =
                _navigationBrush == "Block" ? Color.red
                : _navigationBrush == "Erase" ? Color.yellow
                : Color.green;
            for (var i = 1; i < _navigationBrushRing.Length; i++)
                if (
                    _navigationBrushRingValid[i - 1]
                    && _navigationBrushRingValid[i]
                    && Mathf.Abs(_navigationBrushRing[i].y - _navigationBrushRing[i - 1].y) < .6f
                )
                    Line(new[] { _navigationBrushRing[i - 1], _navigationBrushRing[i] }, color);
        }
        foreach (var link in (_navigationStroke ?? Layout?.Navigation)?.Connections ?? new List<MapNavigationConnection>())
            Line(
                new[] { ZoneRuntime.Vector(link.Start) + Vector3.up * .07f, ZoneRuntime.Vector(link.End) + Vector3.up * .07f },
                Color.yellow
            );
        var start = _navigationLinkStart ?? _navigationPathStart;
        if (start.HasValue && _navigationCursor.HasValue)
            Line(new[] { start.Value, _navigationCursor.Value }, Color.yellow);
    }

    private void DrawNavigationPaint(Camera camera)
    {
        if (_navigationPaintHidden)
            return;
        var revision = (_session?.ContentVersion ?? 0) * 1000000 + _navigationPaintRevision;
        if (
            _navigationPaintLayout != _layoutId
            || _navigationPaintRendered != revision
            || _navigationContourGeometry != SceneNavigation.Revision
            || _navigationContourPending > 0
        )
        {
            if (
                _navigationPaintLayout != _layoutId
                || _navigationContourGeometry != SceneNavigation.Revision
                || _navigationContours.Count > 8192
            )
                _navigationContours.Clear();
            _navigationContourGeometry = SceneNavigation.Revision;
            _navigationContourIncomplete = 0;
            _navigationContourPending = 0;
            var projectedCells = 0;
            var projectionTime = System.Diagnostics.Stopwatch.StartNew();
            _navigationPaintLayout = _layoutId;
            _navigationPaintRendered = revision;
            _navigationAddOverlay ??= new() { Color = new(.15f, 1, .35f, .22f) };
            _navigationBlockOverlay ??= new() { Color = new(1, .1f, .12f, .45f) };
            var recipe = _navigationStroke ?? Layout?.Navigation;
            foreach (var mode in new[] { "Add", "Block" })
            {
                var vertices = new List<Vector3>();
                var indices = new List<int>();
                foreach (var cell in recipe?.Cells ?? new List<MapNavigationCell>())
                {
                    if (cell.Mode != mode)
                        continue;
                    if (_navigationDisplayFloor.HasValue && Math.Abs(cell.Y - _navigationDisplayFloor.Value) > .6f)
                        continue;
                    var key = (cell.X, cell.Z, cell.Y);
                    if (!_navigationContours.TryGetValue(key, out var contour))
                    {
                        if (projectedCells >= 32 || (projectedCells > 0 && projectionTime.ElapsedMilliseconds >= 2))
                        {
                            _navigationContourPending++;
                            continue;
                        }
                        projectedCells++;
                        var sampled = NavigationPaintContour.Build(
                            cell.X,
                            cell.Z,
                            cell.Y,
                            requested =>
                                PaintContourHit(new Vector3(requested.X, requested.Y, requested.Z), out var hit)
                                    ? new System.Numerics.Vector3(hit.x, hit.y, hit.z)
                                    : null
                        );
                        var projected = new Vector3[sampled.Vertices.Length];
                        for (var vertex = 0; vertex < projected.Length; vertex++)
                        {
                            var at = sampled.Vertices[vertex];
                            projected[vertex] = new Vector3(at.X, at.Y, at.Z);
                        }
                        _navigationContours[key] = contour = (projected, sampled.Indices);
                    }
                    if (contour.Indices.Length < 96)
                        _navigationContourIncomplete++;
                    var first = vertices.Count;
                    vertices.AddRange(contour.Vertices);
                    foreach (var index in contour.Indices)
                        indices.Add(first + index);
                }
                (mode == "Add" ? _navigationAddOverlay : _navigationBlockOverlay).Capture(
                    new() { vertices = vertices.ToArray(), indices = indices.ToArray() },
                    null
                );
            }
        }
        _navigationNativeOverlay?.Draw(camera);
        _navigationAddOverlay?.Draw(camera);
        _navigationBlockOverlay?.Draw(camera);
        _navigationPaint?.Draw(camera);
        DrawNavigationHealth(camera);
    }

    private void ViewNavigationPaint(bool floor)
    {
        _navigationPaintHidden = false;
        _navigationDisplayFloor = floor ? NavigationPoint().y : null;
        _navigationPaintRevision++;
        _navigationPaint?.DisplayFloor(_navigationDisplayFloor);
        if (_navigationHealth != null)
            _navigationHealthOverlay?.Capture(
                new() { vertices = _navigationHealth.Vertices.ToArray(), indices = _navigationHealth.Indices.ToArray() },
                _navigationDisplayFloor,
                _navigationHealth.Issues.ToArray()
            );
        _view?.ShowNavigationOverlay();
        _navigationNativeOverlay ??= new() { Color = new(.2f, .65f, 1, .3f) };
        var snapshot = NavMesh.CalculateTriangulation();
        _navigationNativeOverlay.Capture(snapshot, _navigationDisplayFloor);
        _navigationFeedback =
            "Blue: active navigation snapshot · green: Add paint / brighter baked additions · red: Block footprint · yellow: explicit connections.";
    }

    private void StopNavigationPainting()
    {
        ClearNavigationHealth();
        _navigationContours.Clear();
        _navigationBrushRingOrigin = null;
        _navigationContourGeometry = -1;
        _navigationScan?.Cancel();
        _navigationSurvey = null;
        _navigationStroke = null;
        _navigationBrush = "";
        _navigationCursor = _navigationLinkStart = _navigationPathStart = null;
        _navigationPaint?.Dispose();
        _navigationPaint = null;
        _navigationAddOverlay?.Dispose();
        _navigationAddOverlay = null;
        _navigationBlockOverlay?.Dispose();
        _navigationBlockOverlay = null;
        _navigationNativeOverlay?.Dispose();
        _navigationNativeOverlay = null;
        _navigationPaintRendered = -1;
    }
}
