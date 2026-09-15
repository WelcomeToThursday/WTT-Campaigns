using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using WTT.Campaigns.Client.Spatial;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Client.Authoring;

// Toolkit vector overlay; labels and marker batches are pooled and ignore pointer events.
internal sealed class RouteOverlay : VisualElement
{
    private readonly List<(SpatialCapture Point, RouteRole Role, int Number)> _points = new();
    private readonly List<(SpatialCapture Point, RouteRole Role, int Number, string Caption, string Selection)> _aiPoints = new();
    private readonly List<(SpatialCapture From, SpatialCapture To, Color Color)> _aiSegments = new();
    private readonly List<(Vector2 A, Vector2 B, Color Color)> _segments = new();
    private readonly List<(Vector2 Position, RouteRole Role, bool Selected, Color Color)> _markers = new();
    private readonly List<Label> _labels = new();
    private readonly List<VisualElement> _batches = new();
    private const int BatchSize = 400;
    private static readonly Color Dark = new(.025f, .03f, .035f, .95f);
    private readonly Dictionary<string, string> _patrolCaptions = new(StringComparer.Ordinal);
    private MapLayout? _captionLayout;
    private long _captionRevision = long.MinValue;
    private MapLayout? _aiDescriptorLayout;
    private long _aiDescriptorRevision = long.MinValue;

    internal RouteOverlay()
    {
        pickingMode = PickingMode.Ignore;
        style.position = Position.Absolute;
        style.left = style.right = style.top = style.bottom = 0;
        generateVisualContent += context => Populate(context, -1);
    }

    internal static Color RoleColor(RouteRole role)
    {
        var rgb = RouteVisuals.Color(role);
        return new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);
    }

    internal void Refresh(MapLayout layout, Camera camera, string selected, long layoutRevision = 0)
    {
        _segments.Clear();
        _markers.Clear();
        RouteVisuals.Points(layout, _points);
        var near = camera.nearClipPlane + .001f;
        for (var i = 1; i < _points.Count; i++)
        {
            var a = ZoneRuntime.Vector(_points[i - 1].Point.Position);
            var b = ZoneRuntime.Vector(_points[i].Point.Position);
            var az = Vector3.Dot(a - camera.transform.position, camera.transform.forward);
            var bz = Vector3.Dot(b - camera.transform.position, camera.transform.forward);
            if (!RouteVisuals.ClipNear(az, bz, near, out var from, out var to))
                continue;
            var sa = camera.WorldToScreenPoint(Vector3.Lerp(a, b, from));
            var sb = camera.WorldToScreenPoint(Vector3.Lerp(a, b, to));
            if (!RouteVisuals.ClipScreen(ref sa.x, ref sa.y, ref sb.x, ref sb.y, Screen.width, Screen.height))
                continue;
            if (Local(sa, out var la) && Local(sb, out var lb))
                _segments.Add((la, lb, Color.white));
        }

        // AI patrols share this renderer so route clipping, labels, marker
        // outlines, and scenery-through visibility remain identical to player
        // checkpoints. Cache authored descriptors when the layout changes;
        // only their camera projection and clipping are performed per frame.
        RefreshAiDescriptors(layout, layoutRevision);
        foreach (var segment in _aiSegments)
            AddSegment(segment.From, segment.To, camera, near, segment.Color);

        var labelIndex = 0;
        foreach (var point in _points)
            AddMarker(
                point.Point,
                point.Role,
                point.Point.Id == selected,
                RouteVisuals.Label(point.Role, point.Number),
                camera,
                near,
                ref labelIndex
            );
        foreach (var point in _aiPoints)
            AddMarker(point.Point, point.Role, point.Selection == selected, point.Caption, camera, near, ref labelIndex);
        for (var i = labelIndex; i < _labels.Count; i++)
            _labels[i].style.display = DisplayStyle.None;
        var extra = (_markers.Count + BatchSize - 1) / BatchSize;
        for (var i = 0; i < extra; i++)
        {
            if (i == _batches.Count)
            {
                var index = i;
                var batch = new VisualElement { pickingMode = PickingMode.Ignore };
                batch.style.position = Position.Absolute;
                batch.style.left = batch.style.right = batch.style.top = batch.style.bottom = 0;
                batch.generateVisualContent += context => Populate(context, index);
                Insert(0, batch);
                _batches.Add(batch);
            }
            _batches[i].style.display = DisplayStyle.Flex;
            _batches[i].MarkDirtyRepaint();
        }
        for (var i = Math.Max(0, extra); i < _batches.Count; i++)
            _batches[i].style.display = DisplayStyle.None;
        MarkDirtyRepaint();
    }

    private void RefreshAiDescriptors(MapLayout layout, long layoutRevision)
    {
        if (ReferenceEquals(layout, _aiDescriptorLayout) && layoutRevision == _aiDescriptorRevision)
            return;

        _aiDescriptorLayout = layout;
        _aiDescriptorRevision = layoutRevision;
        _aiPoints.Clear();
        _aiSegments.Clear();

        RefreshPatrolCaptions(layout, layoutRevision);
        foreach (var route in (IEnumerable<MapPatrolRoute>?)layout.PatrolRoutes ?? Array.Empty<MapPatrolRoute>())
        {
            if (route?.Waypoints == null)
                continue;
            var caption = _patrolCaptions.GetValueOrDefault(route.Id, "PATROL · " + route.Id);
            for (var i = 0; i < route.Waypoints.Count; i++)
            {
                var waypoint = route.Waypoints[i];
                if (waypoint == null)
                    continue;
                _aiPoints.Add((waypoint, RouteRole.Patrol, i + 1, caption + " · " + (i + 1), "waypoint:" + route.Id + ":" + waypoint.Id));
                if (i > 0 && route.Waypoints[i - 1] != null)
                    _aiSegments.Add((route.Waypoints[i - 1], waypoint, RoleColor(RouteRole.Patrol)));
            }
            if (
                route.Completion == MapPatrolRoute.Loop
                && route.Waypoints.Count > 1
                && route.Waypoints[0] != null
                && route.Waypoints[^1] != null
            )
                _aiSegments.Add((route.Waypoints[^1], route.Waypoints[0], RoleColor(RouteRole.Patrol)));
        }
        foreach (var spawn in (IEnumerable<SpatialCapture>?)layout.SpawnPoints ?? Array.Empty<SpatialCapture>())
            if (spawn != null)
                _aiPoints.Add((spawn, RouteRole.Spawn, 0, "BOT SPAWN", "spawn:" + spawn.Id));
        foreach (var encounter in (IEnumerable<MapEncounter>?)layout.Encounters ?? Array.Empty<MapEncounter>())
            if (encounter?.Trigger?.Volume != null)
                _aiPoints.Add(
                    (
                        encounter.Trigger.Volume,
                        RouteRole.Trigger,
                        0,
                        "TRIGGER · " + (encounter.Trigger.Type ?? MapEncounterTrigger.PlayerEntry),
                        "trigger:" + encounter.Id
                    )
                );
    }

    private void AddMarker(
        SpatialCapture point,
        RouteRole role,
        bool selected,
        string caption,
        Camera camera,
        float near,
        ref int labelIndex
    )
    {
        var screen = camera.WorldToScreenPoint(ZoneRuntime.Vector(point.Position));
        if (!(screen.z >= near && screen.x >= 0 && screen.x <= Screen.width && screen.y >= 0 && screen.y <= Screen.height))
            return;
        if (!Local(screen, out var local))
            return;
        _markers.Add((local, role, selected, RoleColor(role)));
        var label = Label(labelIndex++);
        if (label.text != caption)
            label.text = caption;
        label.style.color = RoleColor(role);
        label.style.left = Mathf.Clamp(local.x + 17, 4, Mathf.Max(4, contentRect.width - 144));
        label.style.top = Mathf.Clamp(local.y - 12, 4, Mathf.Max(4, contentRect.height - 24));
        label.style.display = DisplayStyle.Flex;
    }

    private void AddSegment(SpatialCapture from, SpatialCapture to, Camera camera, float near, Color color)
    {
        var a = ZoneRuntime.Vector(from.Position);
        var b = ZoneRuntime.Vector(to.Position);
        var az = Vector3.Dot(a - camera.transform.position, camera.transform.forward);
        var bz = Vector3.Dot(b - camera.transform.position, camera.transform.forward);
        if (!RouteVisuals.ClipNear(az, bz, near, out var start, out var end))
            return;
        var sa = camera.WorldToScreenPoint(Vector3.Lerp(a, b, start));
        var sb = camera.WorldToScreenPoint(Vector3.Lerp(a, b, end));
        if (!RouteVisuals.ClipScreen(ref sa.x, ref sa.y, ref sb.x, ref sb.y, Screen.width, Screen.height))
            return;
        if (Local(sa, out var la) && Local(sb, out var lb))
            _segments.Add((la, lb, color));
    }

    private void RefreshPatrolCaptions(MapLayout layout, long layoutRevision)
    {
        if (ReferenceEquals(layout, _captionLayout) && layoutRevision == _captionRevision)
            return;
        _captionLayout = layout;
        _captionRevision = layoutRevision;
        _patrolCaptions.Clear();
        foreach (var route in (IEnumerable<MapPatrolRoute>?)layout.PatrolRoutes ?? Array.Empty<MapPatrolRoute>())
        {
            if (route == null)
                continue;
            var squads = new List<string>();
            foreach (var encounter in (IEnumerable<MapEncounter>?)layout.Encounters ?? Array.Empty<MapEncounter>())
            {
                foreach (var wave in (IEnumerable<MapEncounterWave>?)encounter?.Waves ?? Array.Empty<MapEncounterWave>())
                {
                    foreach (var roster in (IEnumerable<MapEncounterRosterEntry>?)wave?.Roster ?? Array.Empty<MapEncounterRosterEntry>())
                    {
                        if (roster == null || roster.PatrolRouteId != route.Id)
                            continue;
                        var label = string.IsNullOrWhiteSpace(roster.SquadId) ? roster.Role : roster.SquadId;
                        if (!string.IsNullOrWhiteSpace(label) && !squads.Contains(label))
                            squads.Add(label);
                    }
                }
            }
            var caption = "PATROL · " + (string.IsNullOrWhiteSpace(route.Name) ? route.Id : route.Name);
            _patrolCaptions[route.Id] = squads.Count == 0 ? caption : caption + " · " + string.Join(", ", squads);
        }
    }

    private bool Local(Vector2 screen, out Vector2 local)
    {
        local = panel == null ? Vector2.zero : RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screen.x, Screen.height - screen.y));
        return panel != null;
    }

    private Label Label(int index)
    {
        if (index == _labels.Count)
        {
            var label = new Label { pickingMode = PickingMode.Ignore, enableRichText = false };
            label.style.position = Position.Absolute;
            label.style.fontSize = 14;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.backgroundColor = Dark;
            label.style.paddingLeft = label.style.paddingRight = 4;
            label.style.maxWidth = 240;
            label.style.overflow = Overflow.Hidden;
            Add(label);
            _labels.Add(label);
        }
        return _labels[index];
    }

    internal void Populate(MeshGenerationContext context, int batch)
    {
        var mesh = context.painter2D;
        var pixel = 1 / Mathf.Max(1, Screen.height / 1080f);
        if (batch < 0)
        {
            // Draw connections behind the separately pooled marker batches.
            foreach (var segment in _segments)
                Stroke(mesh, segment.A, segment.B, 8 * pixel, Dark);
            foreach (var segment in _segments)
                Stroke(mesh, segment.A, segment.B, 4 * pixel, segment.Color);
            return;
        }
        var first = batch * BatchSize;
        for (var i = first; i < Math.Min(first + BatchSize, _markers.Count); i++)
        {
            var marker = _markers[i];
            Glyph(mesh, marker.Position, marker.Role, 9, Dark);
            if (marker.Selected)
                Glyph(mesh, marker.Position, marker.Role, 7, Color.white);
            Glyph(mesh, marker.Position, marker.Role, 3, marker.Color);
        }
    }

    private static void Glyph(Painter2D mesh, Vector2 p, RouteRole role, float width, Color color)
    {
        void Edge(float ax, float ay, float bx, float by) => Stroke(mesh, p + new Vector2(ax, -ay), p + new Vector2(bx, -by), width, color);
        if (role == RouteRole.Start)
        {
            Edge(-7, -11, -7, 11);
            Edge(-7, 11, 10, 5);
            Edge(10, 5, -7, 0);
        }
        else if (role == RouteRole.Checkpoint)
        {
            Edge(0, 11, 10, 0);
            Edge(10, 0, 0, -11);
            Edge(0, -11, -10, 0);
            Edge(-10, 0, 0, 11);
        }
        else
        {
            Edge(-9, -9, -9, 9);
            Edge(-9, 9, 9, 9);
            Edge(9, 9, 9, -9);
            Edge(9, -9, -9, -9);
        }
    }

    private static void Stroke(Painter2D painter, Vector2 a, Vector2 b, float width, Color color)
    {
        if ((b - a).sqrMagnitude < .001f)
            return;
        painter.lineWidth = width;
        painter.strokeColor = color;
        painter.BeginPath();
        painter.MoveTo(a);
        painter.LineTo(b);
        painter.Stroke();
    }
}
