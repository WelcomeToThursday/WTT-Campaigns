using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using WTT.Campaigns.Client.Spatial;
using WTT.Campaigns.Client.Story;
using WTT.Campaigns.Shared.Spatial;
using ZLinq;

namespace WTT.Campaigns.Client.Authoring;

public sealed partial class RaidEditor
{
    private sealed class Drag
    {
        public SpatialCapture Before = null!;
        public Vector2 Mouse,
            Direction;
        public int Axis;
        public float PixelsPerMetre;
    }

    private Drag? _drag;
    private bool _picking;
    private readonly List<LineRenderer> _lines = new();
    private Material? _lineMaterial;
    private int _lineIndex;

    private static Vector3 Axis(int i)
    {
        return i == 0 ? Vector3.right
            : i == 1 ? Vector3.up
            : Vector3.forward;
    }

    private float HandleLength(SpatialCapture point)
    {
        return Mathf.Clamp(Vector3.Distance(_flyPosition, ZoneRuntime.Vector(point.Position)) * .12f, 1, 10);
    }

    private void GeometryInput()
    {
        if (!_camera || _session?.Definition == null)
        {
            return;
        }

        var mouse = (Vector2)Input.mousePosition;
        if (_drag != null)
        {
            if (Input.GetMouseButtonUp(0))
            {
                FinishDrag();
                return;
            }
            var delta = Vector2.Dot(mouse - _drag.Mouse, _drag.Direction) / _drag.PixelsPerMetre;
            var amount = _tool == "Rotate" ? delta * 30 : delta;
            if (_snap && !Input.GetKey(KeyCode.LeftAlt))
            {
                amount = Mathf.Round(amount / (_tool == "Rotate" ? 5 : .05f)) * (_tool == "Rotate" ? 5 : .05f);
            }

            var point = Selected;
            if (point == null)
            {
                CancelDrag();
                return;
            }
            var before = _drag.Before;
            if (_tool == "Move")
            {
                point.Position = ZoneRuntime.Vector(ZoneRuntime.Vector(before.Position) + Axis(_drag.Axis) * amount);
            }
            else if (_tool == "Rotate")
            {
                point.Rotation = ZoneRuntime.Vector(ZoneRuntime.Vector(before.Rotation) + Axis(_drag.Axis) * amount);
            }
            else if (point is SeasonZone zone && before is SeasonZone original)
            {
                if (zone.Shape == "Sphere")
                {
                    zone.Radius = Mathf.Max(.05f, original.Radius + amount);
                }
                else
                {
                    var size = ZoneRuntime.Vector(original.Size);
                    size[_drag.Axis] = Mathf.Max(.05f, size[_drag.Axis] + amount);
                    zone.Size = ZoneRuntime.Vector(size);
                }
            }
            Refresh();
            return;
        }
        if (!Input.GetMouseButtonDown(0) || Input.GetMouseButton(1) || EventSystem.current?.IsPointerOverGameObject() == true)
        {
            return;
        }

        if (_picking)
        {
            if (Physics.Raycast(_camera!.ScreenPointToRay(Input.mousePosition), out var hit, 1000, ~0, QueryTriggerInteraction.Collide))
            {
                _picked = hit.transform;
                _picking = false;
                _notice = "Scene target selected. Check its components, then use the target.";
                Refresh();
            }
            return;
        }
        if (Selected is { } selected)
        {
            var origin = _camera!.WorldToScreenPoint(ZoneRuntime.Vector(selected.Position));
            if (origin.z > 0)
            {
                for (var axis = 0; axis < 3; axis++)
                {
                    var length = HandleLength(selected);
                    var end = _camera.WorldToScreenPoint(ZoneRuntime.Vector(selected.Position) + Axis(axis) * length);
                    var direction = (Vector2)(end - origin);
                    if (direction.magnitude < 12)
                    {
                        continue;
                    }

                    var along = Vector2.Dot(mouse - (Vector2)origin, direction.normalized);
                    var distance = Vector2.Distance(
                        mouse,
                        (Vector2)origin + direction.normalized * Mathf.Clamp(along, 0, direction.magnitude)
                    );
                    if (along < 15 || distance > 12)
                    {
                        continue;
                    }

                    _drag = new Drag
                    {
                        Before = selected is SeasonZone z ? RaidEditorSession.Copy(z) : RaidEditorSession.Copy(selected),
                        Axis = axis,
                        Mouse = mouse,
                        Direction = direction.normalized,
                        PixelsPerMetre = direction.magnitude / length,
                    };
                    return;
                }
            }
        }
        var closest = _session
            .Definition.Zones.AsValueEnumerable()
            .Where(z => z.Location == _session.Location)
            .Select(z => (Zone: z, Screen: _camera!.WorldToScreenPoint(ZoneRuntime.Vector(z.Position))))
            .Where(z => z.Screen.z > 0)
            .OrderBy(z => Vector2.Distance(mouse, z.Screen))
            .FirstOrDefault();
        if (closest.Zone != null && Vector2.Distance(mouse, closest.Screen) < 24)
        {
            _selected = closest.Zone.Id;
            _mode = "Zones";
            _picked = null;
            Refresh();
        }
    }

    private void RestorePoint(SpatialCapture target, SpatialCapture source)
    {
        target.Position = RaidEditorSession.Copy(source.Position);
        target.Rotation = RaidEditorSession.Copy(source.Rotation);
        if (target is SeasonZone zone && source is SeasonZone original)
        {
            zone.Size = RaidEditorSession.Copy(original.Size);
            zone.Radius = original.Radius;
        }
    }

    private void FinishDrag()
    {
        if (_drag == null || Selected == null)
        {
            return;
        }

        var point = Selected;
        var after = point is SeasonZone zone ? RaidEditorSession.Copy(zone) : RaidEditorSession.Copy(point);
        RestorePoint(point, _drag.Before);
        _drag = null;
        EditPoint(p => RestorePoint(p, after));
    }

    private void CancelDrag()
    {
        if (_drag == null)
        {
            return;
        }

        if (Selected != null)
        {
            RestorePoint(Selected, _drag.Before);
        }

        _drag = null;
        Refresh();
    }

    internal static bool Inside(SeasonZone zone, Vector3 position)
    {
        var local =
            Quaternion.Inverse(Quaternion.Euler(ZoneRuntime.Vector(zone.Rotation))) * (position - ZoneRuntime.Vector(zone.Position));
        return zone.Shape == "Sphere"
            ? local.sqrMagnitude <= zone.Radius * zone.Radius
            : Mathf.Abs(local.x) <= zone.Size.X / 2 && Mathf.Abs(local.y) <= zone.Size.Y / 2 && Mathf.Abs(local.z) <= zone.Size.Z / 2;
    }

    private void DrawGeometry()
    {
        if (!_open || _session?.Definition == null)
        {
            return;
        }

        _lineIndex = 0;
        foreach (
            var zone in _session
                .Definition.Zones.AsValueEnumerable()
                .Where(z => z.Location == _session.Location)
                .OrderBy(z => z.Id == _selected ? 0 : 1)
                .ThenBy(z => Vector3.Distance(_flyPosition, ZoneRuntime.Vector(z.Position)))
                .Take(100)
        )
        {
            var color = zone.Id == _selected ? new Color(.85f, .78f, .45f) : new Color(.4f, .7f, .6f, .7f);
            var center = ZoneRuntime.Vector(zone.Position);
            var rotation = Quaternion.Euler(ZoneRuntime.Vector(zone.Rotation));
            if (zone.Shape == "Sphere")
            {
                for (var axis = 0; axis < 3; axis++)
                {
                    var points = new Vector3[49];
                    for (var i = 0; i < points.Length; i++)
                    {
                        var angle = i / 48f * Mathf.PI * 2;
                        points[i] =
                            center
                            + rotation * (Axis((axis + 1) % 3) * Mathf.Cos(angle) + Axis((axis + 2) % 3) * Mathf.Sin(angle)) * zone.Radius;
                    }
                    Line(points, color);
                }
            }
            else
            {
                var corners = new Vector3[8];
                for (var i = 0; i < 8; i++)
                {
                    corners[i] =
                        center
                        + rotation
                            * Vector3.Scale(
                                ZoneRuntime.Vector(zone.Size) * .5f,
                                new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)
                            );
                }

                for (var i = 0; i < 8; i++)
                {
                    for (var axis = 0; axis < 3; axis++)
                    {
                        if ((i & (1 << axis)) == 0)
                        {
                            Line(new[] { corners[i], corners[i | (1 << axis)] }, color);
                        }
                    }
                }
            }
            Line(new[] { center - Vector3.right * .15f, center + Vector3.right * .15f }, color);
            Line(new[] { center - Vector3.up * .15f, center + Vector3.up * .15f }, color);
        }
        if (Selected is { } selected)
        {
            for (var axis = 0; axis < 3; axis++)
            {
                var center = ZoneRuntime.Vector(selected.Position);
                Line(
                    new[] { center, center + Axis(axis) * HandleLength(selected) },
                    axis == 0 ? Color.red
                        : axis == 1 ? Color.green
                        : Color.cyan,
                    .04f
                );
            }
        }

        for (var i = _lineIndex; i < _lines.Count; i++)
        {
            _lines[i].gameObject.SetActive(false);
        }
    }

    private void Line(Vector3[] points, Color color, float width = .025f)
    {
        if (!_lineMaterial)
        {
            _lineMaterial = new Material(Shader.Find("Hidden/Internal-Colored") ?? Shader.Find("Sprites/Default"));
            _lineMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            _lineMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            _lineMaterial.SetInt("_ZWrite", 0);
        }
        if (_lineIndex == _lines.Count)
        {
            var root = new GameObject("Campaign authoring outline");
            var line = root.AddComponent<LineRenderer>();
            line.sharedMaterial = _lineMaterial;
            line.useWorldSpace = true;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            _lines.Add(line);
        }
        var renderer = _lines[_lineIndex++];
        renderer.gameObject.SetActive(true);
        renderer.positionCount = points.Length;
        renderer.SetPositions(points);
        renderer.startColor = renderer.endColor = color;
        renderer.startWidth = renderer.endWidth = width;
    }

    private void ClearLines()
    {
        foreach (var line in _lines.AsValueEnumerable().Where(static l => l))
        {
            Destroy(line.gameObject);
        }

        _lines.Clear();
        if (_lineMaterial)
        {
            Destroy(_lineMaterial);
        }

        _lineMaterial = null;
    }
}
