using System.Globalization;
using Cysharp.Threading.Tasks;
using UnityEngine;
using WTT.Campaigns.Client.Authoring.Console;
using WTT.Campaigns.Client.Authoring.Scenes;
using WTT.Campaigns.Client.Authoring.Views;
using WTT.Campaigns.Client.Encounters;
using WTT.Campaigns.Shared.Spatial;
using WTT.Campaigns.UI.Controls;

namespace WTT.Campaigns.Client.Authoring.Controllers;

/// <summary>One editor for all route owners; geometry and transactions stay independent of patrol gameplay.</summary>
internal sealed class EditorSplineController(IEditorAiContext context)
{
    private readonly EncounterNavigation _navigation = new(() => context.Layout);
    private string _owner = "",
        _knot = "",
        _selection = "";
    private int _part; // 0 knot, 1 incoming, 2 outgoing
    private bool _editing,
        _busy;
    private SpatialSpline? _before;
    private MapLayout? _dragLayout;
    private Vector3 _dragOrigin,
        _dragHit;
    private Plane _plane;
    private int _dragAxis = -1;
    internal bool Dragging => _before != null;
    internal bool Editing => _editing && Available && Spline != null && Owner == _owner;
    private MapPatrolRoute? Route
    {
        get
        {
            if (context.ToolId != "AI")
                return null;
            var parts = context.SelectionId.Split(':');
            return parts.Length > 1 && parts[0] is "route" or "waypoint" ? context.Layout?.PatrolRoutes.Find(r => r.Id == parts[1]) : null;
        }
    }
    private string Owner => context.LayoutId + ":" + (Route?.Id ?? "player");
    private bool Available => context.Layout != null && (context.ToolId == "Routes" || Route != null);
    private bool CanEdit =>
        Available
        && EditorMode.Ready
        && context.IsOpen
        && !context.AiPreviewBusy
        && context.Session is { Definition: not null, Conflict: null, Retired: false, Previewing: false };
    private SpatialSpline? Spline => Route?.Spline ?? (context.ToolId == "Routes" ? context.Layout?.PlayerRouteSpline : null);
    private List<SpatialCapture> Anchors => Route?.Waypoints ?? RouteSpline.PlayerAnchors(context.Layout!);
    private SplineKnot? Selected => Spline?.Knots.Find(k => k.Id == _knot);
    private static readonly string[] Modes = { SplineKnot.Corner, SplineKnot.Auto, SplineKnot.Aligned, SplineKnot.Free };

    internal void Bind(RaidEditorView view)
    {
        view.Button("SplineEdit", () => Run(Enable));
        view.Button("SplineSmoothSelected", () => Run(() => Smooth(false)));
        view.Button("SplineSmoothRoute", () => Run(() => Smooth(true)));
        view.Button(
            "SplineCorner",
            () =>
                Edit(s =>
                {
                    if (s.Knots.Find(k => k.Id == _knot) is { } k)
                    {
                        k.Mode = SplineKnot.Corner;
                        k.Incoming = new();
                        k.Outgoing = new();
                    }
                })
        );
        view.Button("SplineInsert", Insert);
        view.Button("SplineDelete", () => Edit(s => s.Knots.RemoveAll(k => k.Id == _knot && k.AnchorId.Length == 0)));
        view.Dropdown(
            "SplineMode",
            index =>
                Edit(s =>
                {
                    var i = s.Knots.FindIndex(k => k.Id == _knot);
                    if (i < 0)
                        return;
                    s.Knots[i].Mode = Modes[index];
                    if (index == 0)
                    {
                        s.Knots[i].Incoming = new();
                        s.Knots[i].Outgoing = new();
                    }
                    if (index == 1)
                        SplineGeometry.Smooth(s, i, s.Strength);
                    if (index == 2)
                        SplineGeometry.SetHandle(s.Knots[i], false, SplineGeometry.Vector(s.Knots[i].Outgoing));
                })
        );
        view.Dropdown(
            "SplinePart",
            index =>
            {
                _part = index;
                context.Refresh(false);
            }
        );
        view.Input("SplineStrength", value => Number(value, number => Edit(s => s.Strength = Math.Clamp(number / 100, 0, 1))));
        for (var i = 0; i < 3; i++)
        {
            var axis = i;
            view.Input(
                "Spline" + "XYZ"[i],
                value =>
                    Number(
                        value,
                        number =>
                            Edit(s =>
                            {
                                var k = s.Knots.Find(k => k.Id == _knot);
                                if (k == null)
                                    return;
                                var position = World(k, _part);
                                position[axis] = number;
                                SetPosition(s, k, position, axis != 1);
                            })
                    )
            );
        }
    }

    private async void Run(Func<UniTask> action)
    {
        if (!CanEdit || _busy)
            return;
        CancelDrag();
        _busy = true;
        context.Session!.Hold = true;
        context.Refresh(false);
        try
        {
            await action();
        }
        catch (Exception e)
        {
            context.ReportFeedback(e.Message, ConsoleSeverity.Error);
        }
        finally
        {
            _busy = false;
            context.Refresh();
        }
    }

    internal bool Busy => _busy;

    private async UniTask Enable()
    {
        _owner = Owner;
        if (Spline != null)
        {
            _editing = !_editing;
            return;
        }
        var layout = context.Layout!;
        var route = Route;
        var revision = context.Session!.ContentVersion;
        var navigationRevision = SceneNavigation.Revision;
        var anchors = Anchors;
        if (anchors.Count < 2)
            throw new InvalidOperationException("Add at least two route markers before enabling spline editing.");
        var spline = RouteSpline.Create(anchors, route?.Completion == MapPatrolRoute.Loop);
        if (route != null)
        {
            var knots = new List<SplineKnot>();
            for (var i = 0; i < anchors.Count; i++)
            {
                knots.Add(spline.Knots[i]);
                if (i == anchors.Count - 1 && !spline.Closed)
                    break;
                var path = _navigation.EvaluatePath(anchors[i].Position, anchors[(i + 1) % anchors.Count].Position);
                if (path.Status != EncounterPathStatus.Complete)
                    throw new InvalidOperationException(path.Reason);
                for (var j = 1; j < path.Corners.Length - 1; j++)
                    knots.Add(new() { Position = RaidEditorSession.Copy(path.Corners[j]) });
                await UniTask.Yield();
                if (
                    !CanEdit
                    || context.Layout != layout
                    || revision != context.Session.ContentVersion
                    || SceneNavigation.Revision != navigationRevision
                    || Owner != _owner
                )
                    throw new InvalidOperationException("The route or navigation changed. Enable spline editing again.");
            }
            spline.Knots = knots;
        }
        Commit(spline);
        _editing = true;
        _knot = spline.Knots[0].Id;
    }

    private async UniTask Smooth(bool all)
    {
        if (Spline == null)
        {
            await Enable();
            if (Spline == null)
                return;
        }
        var source = Spline!;
        var candidate = RouteSpline.Resolved(source, Anchors);
        var revision = context.Session!.ContentVersion;
        var navRevision = SceneNavigation.Revision;
        var owner = Owner;
        var ids = new List<string>();
        foreach (var k in candidate.Knots)
            if (all || k.Id == _knot)
                ids.Add(k.Id);
        if (ids.Count == 0)
            return;
        var reduced = 0;
        foreach (var id in ids)
        {
            var index = candidate.Knots.FindIndex(k => k.Id == id);
            var accepted = false;
            for (var attempt = 0; attempt < 7; attempt++)
            {
                var trial = RaidEditorSession.Copy(candidate);
                var strength = source.Strength / (1 << attempt);
                if (trial.Knots[index].AnchorId.Length == 0)
                    SplineGeometry.RoundCorner(trial, index, strength);
                else
                    SplineGeometry.Smooth(trial, index, strength);
                if (Route == null)
                {
                    candidate = trial;
                    accepted = true;
                    break;
                }
                var check = _navigation.CheckCurve(trial);
                while (check.Pending)
                {
                    var quota = 8;
                    check.Advance(() => quota-- > 0);
                    await UniTask.Yield();
                    if (!CanEdit || Owner != owner || context.Session.ContentVersion != revision || SceneNavigation.Revision != navRevision)
                        throw new InvalidOperationException("The route or navigation changed. Smooth the route again.");
                }
                if (!check.Complete)
                    continue;
                // A reduced auto handle is an explicit validated result; do not regrow it on unrelated anchor edits.
                if (attempt > 0 && trial.Knots[index].Mode == SplineKnot.Auto)
                    trial.Knots[index].Mode = SplineKnot.Aligned;
                candidate = trial;
                accepted = true;
                reduced += attempt > 0 ? 1 : 0;
                break;
            }
            if (!accepted)
                reduced++;
        }
        Commit(candidate);
        _editing = true;
        _owner = Owner;
        context.ReportFeedback(
            reduced > 0 ? $"Smoothed route. {reduced} corner(s) kept tighter for clearance." : "Curve smoothing applied."
        );
    }

    private void Number(string text, Action<float> action)
    {
        if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && float.IsFinite(value))
            action(value);
        else
            context.ReportFeedback("Enter a finite number.", ConsoleSeverity.Error);
    }

    private void Edit(Action<SpatialSpline> edit)
    {
        if (!CanEdit || _busy || Spline == null)
            return;
        CancelDrag();
        try
        {
            var copy = RouteSpline.Resolved(Spline!, Anchors);
            edit(copy);
            Commit(copy);
        }
        catch (Exception e)
        {
            context.ReportFeedback(e.Message, ConsoleSeverity.Error);
        }
        context.Refresh();
    }

    private void Commit(SpatialSpline spline)
    {
        var layoutId = context.LayoutId;
        var routeId = Route?.Id;
        context.Session!.Edit(definition =>
        {
            var layout = definition.MapLayouts.Find(l => l.Id == layoutId)!;
            if (routeId == null)
                layout.PlayerRouteSpline = spline;
            else
                layout.PatrolRoutes.Find(r => r.Id == routeId)!.Spline = spline;
            var anchors = routeId == null ? RouteSpline.PlayerAnchors(layout) : layout.PatrolRoutes.Find(r => r.Id == routeId)!.Waypoints;
            foreach (var knot in spline.Knots)
                if (knot.AnchorId.Length > 0 && anchors.Find(p => p.Id == knot.AnchorId) is { } anchor)
                    anchor.Position = RaidEditorSession.Copy(knot.Position);
        });
    }

    private void Insert() =>
        Edit(s =>
        {
            var i = s.Knots.FindIndex(k => k.Id == _knot);
            if (i >= SplineGeometry.SegmentCount(s))
                i--;
            if (i >= 0)
            {
                _knot = SplineGeometry.Split(s, i, .5f).Id;
                _part = 0;
            }
        });

    internal void Present()
    {
        var view = context.View;
        if (view?.Valid != true)
            return;
        if (Owner != _owner)
        {
            CancelDrag();
            _editing = false;
            _knot = "";
            _owner = Owner;
        }
        view.Visible("SplineSection", Available);
        if (!Available)
            return;
        var spline = Spline;
        if (_selection != context.SelectionId)
        {
            CancelDrag();
            _selection = context.SelectionId;
            var parts = _selection.Split(':');
            var anchorId = parts.Length == 3 && parts[0] == "waypoint" ? parts[2] : _selection;
            if (spline?.Knots.Find(k => k.AnchorId == anchorId) is { } anchor)
            {
                _knot = anchor.Id;
                _part = 0;
            }
        }
        if (spline != null && Selected == null && spline.Knots.Count > 0)
            _knot = spline.Knots[0].Id;
        view.Caption("SplineEdit", Editing ? "Finish spline editing" : "Edit Spline");
        view.Text(
            "SplineStatus",
            _busy ? "Checking curve…"
                : spline == null ? "Enable curves to shape this route."
                : Selected is { } knot
                    ? $"{(knot.AnchorId.Length > 0 ? "Route anchor" : "Shaping point")} · {spline.Knots.FindIndex(k => k.Id == _knot) + 1} / {spline.Knots.Count}"
                : "Select a curve point."
        );
        view.Visible("SplineDetails", spline != null);
        foreach (
            var id in new[] { "SplineEdit", "SplineSmoothSelected", "SplineSmoothRoute", "SplineCorner", "SplineInsert", "SplineDelete" }
        )
            view.Get<EditorButton>(id).interactable =
                CanEdit && !_busy && !Dragging && (id != "SplineDelete" || Selected?.AnchorId.Length == 0);
        if (spline == null)
            return;
        view.Value("SplineStrength", (spline.Strength * 100).ToString("0.#", CultureInfo.InvariantCulture));
        view.SetDropdown(
            "SplineMode",
            new List<EditorChoice.OptionData> { new("Corner"), new("Auto"), new("Aligned"), new("Free") },
            Math.Max(0, Array.IndexOf(Modes, Selected?.Mode))
        );
        view.SetDropdown(
            "SplinePart",
            new List<EditorChoice.OptionData> { new("Point"), new("Incoming handle"), new("Outgoing handle") },
            _part
        );
        foreach (var id in new[] { "SplineMode", "SplinePart", "SplineStrength", "SplineX", "SplineY", "SplineZ" })
            view.Element(id).SetEnabled(CanEdit && !_busy && !Dragging);
        if (Selected is { } selected)
        {
            var value = World(selected, _part);
            for (var axis = 0; axis < 3; axis++)
                view.Value("Spline" + "XYZ"[axis], value[axis].ToString("0.###", CultureInfo.InvariantCulture));
        }
    }

    private static Vector3 V(SpatialVector p) => new(p.X, p.Y, p.Z);

    private static SpatialVector S(Vector3 p) =>
        new()
        {
            X = p.x,
            Y = p.y,
            Z = p.z,
        };

    private static Vector3 World(SplineKnot k, int part) =>
        V(k.Position)
        + (
            part == 1 ? V(k.Incoming)
            : part == 2 ? V(k.Outgoing)
            : Vector3.zero
        );

    private bool SetPosition(SpatialSpline spline, SplineKnot k, Vector3 position, bool followGround)
    {
        if (followGround && Route != null)
        {
            if (_part == 0)
                position = _navigation.GroundCurveEdit(position, V(k.Position));
            else
                position.y = World(k, _part).y;
        }
        // Compare after grounding: the snapped floor can differ from the mouse
        // plane/grid even while the pointer is stationary.
        if (World(k, _part) == position)
            return false;
        if (_part == 0)
        {
            k.Position = S(position);
            var i = spline.Knots.IndexOf(k);
            for (var j = 0; j < spline.Knots.Count; j++)
                if (
                    (j == i || j == i - 1 || j == i + 1 || (spline.Closed && (Math.Abs(j - i) == spline.Knots.Count - 1)))
                    && spline.Knots[j].Mode == SplineKnot.Auto
                )
                    SplineGeometry.Smooth(spline, j, spline.Strength);
        }
        else
            SplineGeometry.SetHandle(k, _part == 1, SplineGeometry.Vector(S(position - V(k.Position))));
        return true;
    }

    internal bool Input(bool snap)
    {
        if (!Editing || !CanEdit || _busy || context.Camera == null || context.View?.ViewportState.Shows(EditorOverlays.Handles) != true)
        {
            CancelDrag();
            return false;
        }
        var camera = context.Camera;
        var mouse = (Vector2)UnityEngine.Input.mousePosition;
        if (Dragging)
        {
            if (!UnityEngine.Input.GetMouseButton(0))
            {
                var after = RaidEditorSession.Copy(Spline!);
                RestoreDrag();
                Commit(after);
                context.Refresh();
                return true;
            }
            if (Selected == null)
            {
                CancelDrag();
                return true;
            }
            var ray = camera.EditorScreenPointToRay(mouse);
            if (_plane.Raycast(ray, out var distance))
            {
                var delta = ray.GetPoint(distance) - _dragHit;
                if (_dragAxis >= 0)
                {
                    var amount = delta[_dragAxis];
                    delta = Vector3.zero;
                    delta[_dragAxis] = amount;
                }
                var position = _dragOrigin + delta;
                if (snap && !UnityEngine.Input.GetKey(KeyCode.LeftAlt))
                    for (var i = 0; i < 3; i++)
                        position[i] = Mathf.Round(position[i] / .05f) * .05f;
                if (World(Selected, _part) == position)
                    return true;
                if (!SetPosition(Spline!, Selected, position, _dragAxis != 1))
                    return true;
                position = World(Selected, _part);
                context.View!.RoutePreviewRevision++;
                if (_part == 0 && Selected.AnchorId.Length > 0 && Anchors.Find(p => p.Id == Selected.AnchorId) is { } a)
                    a.Position = S(position);
                // Camera rendering reads the live draft each frame. Only these
                // coordinate fields need updating during a drag, not the whole editor.
                for (var i = 0; i < 3; i++)
                    context.View.Value("Spline" + "XYZ"[i], position[i].ToString("0.###", CultureInfo.InvariantCulture));
            }
            return true;
        }
        if (!UnityEngine.Input.GetMouseButtonDown(0) || UnityEngine.Input.GetMouseButton(1) || context.View!.PointerOver)
            return false;
        var best = 13f;
        SplineKnot? hit = null;
        var part = 0;
        var axisHit = -1;
        foreach (var k in Spline!.Knots)
            for (var p = 0; p < (k.Id == _knot && k.Mode != SplineKnot.Corner ? 3 : 1); p++)
            {
                var screen = camera.EditorWorldToScreenPoint(World(k, p));
                var distance = Vector2.Distance(mouse, screen);
                if (screen.z > camera.nearClipPlane && distance < best)
                {
                    best = distance;
                    hit = k;
                    part = p;
                }
            }
        if (Selected is { } current)
        {
            var origin = World(current, _part);
            var scale = SceneHandleMath.MetresPerPixel(camera, origin) * 70;
            for (var a = 0; a < 3; a++)
            {
                var axis = Vector3.zero;
                axis[a] = 1;
                var distance = SceneHandleMath.HitPath(camera, new[] { origin + axis * scale * .2f, origin + axis * scale }, mouse, out _);
                if (distance < best)
                {
                    best = distance;
                    hit = current;
                    part = _part;
                    axisHit = a;
                }
            }
        }
        if (hit == null)
        {
            if (UnityEngine.Input.GetKey(KeyCode.LeftControl))
            {
                var samples = SplineGeometry.Sample(Spline!);
                var bestDistance = 12f;
                var segment = -1;
                var t = .5f;
                for (var i = 1; i < samples.Count; i++)
                {
                    var a = V(SplineGeometry.Spatial(samples[i - 1].Position));
                    var b = V(SplineGeometry.Spatial(samples[i].Position));
                    var distance = SceneHandleMath.HitPath(camera, new[] { a, b }, mouse, out _);
                    if (distance >= bestDistance)
                        continue;
                    bestDistance = distance;
                    segment = samples[i].Segment;
                    t = Math.Clamp((samples[i].T + (samples[i - 1].Segment == segment ? samples[i - 1].T : 0)) / 2, .001f, .999f);
                }
                if (segment >= 0)
                {
                    Edit(s => _knot = SplineGeometry.Split(s, segment, t).Id);
                    _part = 0;
                    return true;
                }
            }
            return true;
        }
        _knot = hit.Id;
        _part = part;
        _dragAxis = axisHit;
        _dragOrigin = World(hit, part);
        var normal = camera.transform.forward;
        if (axisHit >= 0)
        {
            var axis = Vector3.zero;
            axis[axisHit] = 1;
            normal -= axis * Vector3.Dot(normal, axis);
        }
        if (normal.sqrMagnitude < .001f)
            return true;
        _plane = new Plane(normal.normalized, _dragOrigin);
        if (_plane.Raycast(camera.EditorScreenPointToRay(mouse), out var hitDistance))
        {
            _dragHit = camera.EditorScreenPointToRay(mouse).GetPoint(hitDistance);
            _before = RaidEditorSession.Copy(Spline!);
            _dragLayout = context.Layout;
            context.Session!.Hold = true;
        }
        context.Refresh(false);
        return true;
    }

    private void RestoreDrag()
    {
        if (_before == null || _dragLayout == null)
            return;
        var before = _before;
        _before = null;
        var routeId = _owner.Substring(_owner.IndexOf(':') + 1);
        var route = _dragLayout.PatrolRoutes.Find(r => r.Id == routeId);
        if (route == null)
            _dragLayout.PlayerRouteSpline = before;
        else
            route.Spline = before;
        var anchors = route?.Waypoints ?? RouteSpline.PlayerAnchors(_dragLayout);
        foreach (var k in before.Knots)
            if (anchors.Find(p => p.Id == k.AnchorId) is { } anchor)
                anchor.Position = RaidEditorSession.Copy(k.Position);
        _dragLayout = null;
        if (context.View != null)
            context.View.RoutePreviewRevision++;
    }

    internal void CancelDrag()
    {
        if (Dragging)
            RestoreDrag();
    }

    internal void Draw(Action<Vector3[], Color, float, bool> line)
    {
        if (!Editing || context.Camera == null || context.View?.ViewportState.Shows(EditorOverlays.Handles) != true)
            return;
        var camera = context.Camera;
        foreach (var k in Spline!.Knots)
        {
            var p = V(k.Position);
            var selected = k.Id == _knot;
            Marker(p, selected ? Color.yellow : Color.white, selected ? 6 : 4);
            if (!selected || k.Mode == SplineKnot.Corner)
                continue;
            for (var part = 1; part <= 2; part++)
            {
                var endpoint = World(k, part);
                var color = part == _part ? Color.yellow : Color.cyan;
                line(new[] { p, endpoint }, color, SceneHandleMath.MetresPerPixel(camera, p) * 2, true);
                Marker(endpoint, color, 5);
            }
        }
        if (Selected is { } knot)
        {
            var origin = World(knot, _part);
            var pixel = SceneHandleMath.MetresPerPixel(camera, origin);
            for (var a = 0; a < 3; a++)
            {
                var delta = Vector3.zero;
                delta[a] = pixel * 70;
                line(
                    new[] { origin + delta * .2f, origin + delta },
                    a == 0 ? Color.red
                        : a == 1 ? Color.green
                        : Color.cyan,
                    pixel * 3,
                    true
                );
            }
        }
        void Marker(Vector3 position, Color color, int radius)
        {
            if (camera.EditorWorldToScreenPoint(position).z <= camera.nearClipPlane)
                return;
            var pixel = SceneHandleMath.MetresPerPixel(camera, position);
            var x = camera.transform.right * pixel * radius;
            var y = camera.transform.up * pixel * radius;
            line(
                new[] { position - x - y, position + x - y, position + x + y, position - x + y, position - x - y },
                color,
                pixel * 2,
                true
            );
        }
    }
}
