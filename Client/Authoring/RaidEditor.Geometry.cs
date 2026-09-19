using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using WTT.Campaigns.Client.Authoring.Scenes;
using WTT.Campaigns.Client.Spatial;
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
        public Vector3 Anchor,
            LocalAnchor;
        public Transform? AnchorTarget;
        public bool Centered,
            Transient,
            RotationPlane;
        public Vector3 RotationStart;
        public float PixelsPerMetre;
    }

    private Drag? _drag;
    internal bool IsDragging => _drag != null;
    private bool _picking;
    private readonly List<LineRenderer> _lines = new();
    private Material? _lineMaterial,
        _handleMaterial;
    private int _lineIndex;

    private static Vector3 Axis(int i)
    {
        return i == 0 ? Vector3.right
            : i == 1 ? Vector3.up
            : Vector3.forward;
    }

    private float HandleLength(SpatialCapture point)
    {
        return _camera ? WTT.Campaigns.UI.Controls.SceneHandleMath.MetresPerPixel(_camera!, HandleOrigin(point)) * 90 : 1;
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
            var pixels = Vector2.Dot(mouse - _drag.Mouse, _drag.Direction);
            var amount = _tool == "Rotate" ? pixels : delta;
            if (_tool == "Rotate" && _drag.RotationPlane)
            {
                var plane = new Plane(Axis(_drag.Axis), _drag.Anchor);
                var ray = _camera!.ScreenPointToRay(mouse);
                if (plane.Raycast(ray, out var distance))
                    amount = Vector3.SignedAngle(_drag.RotationStart, ray.GetPoint(distance) - _drag.Anchor, Axis(_drag.Axis));
            }
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
                point.Rotation = ZoneRuntime.Vector(
                    WTT.Campaigns.UI.Controls.SceneSelectionGeometry.Rotation(
                        Quaternion.Euler(ZoneRuntime.Vector(before.Rotation)),
                        _drag.Axis,
                        amount
                    ).eulerAngles
                );
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
            if (_tool == "Scale" && point is MapVolume volume && before is MapVolume sourceVolume)
            {
                var size = ZoneRuntime.Vector(sourceVolume.Size);
                size[_drag.Axis] = Mathf.Max(.05f, size[_drag.Axis] + amount);
                if (volume.Shape == "Sphere")
                {
                    volume.Radius = size[_drag.Axis] / 2;
                    size = Vector3.one * volume.Radius * 2;
                }
                volume.Size = ZoneRuntime.Vector(size);
            }
            if (
                _tool == "Scale"
                && point is MapObjectEdit { Target.Kind: "Prop" or "AssetProp" } obj
                && before is MapObjectEdit sourceObject
            )
            {
                var scale = ZoneRuntime.Vector(sourceObject.Scale);
                scale[_drag.Axis] = WTT.Campaigns.UI.Controls.SceneSelectionGeometry.Resize(
                    scale[_drag.Axis],
                    pixels,
                    _snap && !Input.GetKey(KeyCode.LeftAlt)
                );
                obj.Scale = ZoneRuntime.Vector(scale);
            }
            if (_mode == "AI" && !Ai.AiAcceptPreview(point, before))
            {
                Refresh();
                return;
            }
            KeepDragAnchor(point, _drag);
            Refresh();
            return;
        }
        if (
            !Input.GetMouseButtonDown(0)
            || Input.GetMouseButton(1)
            || EventSystem.current?.IsPointerOverGameObject() == true
            || _view?.PointerOver == true
        )
        {
            return;
        }

        if (_picking)
        {
            if ((_mode == "Maps" || _mode == "Scene") && ScenePicking.Dispatch(EditorMode.Ready, _mode, PickScene))
            {
                _picking = Catalog.RebindId.Length > 0;
                return;
            }
            if (Physics.Raycast(_camera!.ScreenPointToRay(Input.mousePosition), out var hit, 1000, ~0, QueryTriggerInteraction.Collide))
            {
                _picked = hit.transform;
                _picking = false;
                _notice = "Scene target selected. Check its components, then use the target.";
                Refresh();
            }
            return;
        }
        if (Selected is { } selected && CanUseHandle(selected))
        {
            var axis = HoverHandle(selected);
            if (axis >= 0)
            {
                var world = HandleOrigin(selected);
                var origin = _camera!.WorldToScreenPoint(world);
                var length = HandleLength(selected);
                var end = _camera.WorldToScreenPoint(world + HandleAxis(selected, axis) * length);
                var direction = (Vector2)(end - origin);
                if (_tool == "Rotate")
                    direction = HoverTangent(selected, axis);
                _drag = new Drag
                {
                    Before = CopyPoint(selected),
                    Axis = axis,
                    Mouse = mouse,
                    Direction = direction.normalized,
                    PixelsPerMetre = direction.magnitude / length,
                    Anchor = world,
                    Centered = Catalog.SceneWorkspace && _centerAnchor,
                    AnchorTarget = Catalog.SceneWorkspace ? SceneSelectionTarget : null,
                    LocalAnchor =
                        Catalog.SceneWorkspace && SceneSelectionTarget ? SceneSelectionTarget!.InverseTransformPoint(world) : Vector3.zero,
                    Transient = Catalog.SceneWorkspace && Maps.MapPoint == null,
                };
                if (_tool == "Rotate")
                {
                    var ray = _camera.ScreenPointToRay(mouse);
                    var plane = new Plane(Axis(axis), world);
                    _drag.RotationPlane = plane.Raycast(ray, out var distance);
                    if (_drag.RotationPlane)
                        _drag.RotationStart = (ray.GetPoint(distance) - world).normalized;
                }
                _session.Hold = true;
                return;
            }
        }
        if (_mode is not ("Zones" or "Hazards") && ScenePicking.Dispatch(EditorMode.Ready, _mode, PickScene))
            return;
        var closest = FilterZonesForLayout(_layoutId)
            .AsValueEnumerable()
            .Select(z => (Zone: z, Screen: _camera!.WorldToScreenPoint(ZoneRuntime.Vector(z.Position))))
            .Where(z => z.Screen.z > 0)
            .OrderBy(z => Vector2.Distance(mouse, z.Screen))
            .FirstOrDefault();
        if (closest.Zone != null && Vector2.Distance(mouse, closest.Screen) < 24)
        {
            _selected = closest.Zone.Id;
            _mode = closest.Zone.Hazard == null ? "Zones" : "Hazards";
            _picked = null;
            Refresh();
            return;
        }
        if (EditorMode.Ready)
            PickScene();
    }

    private static SpatialCapture CopyPoint(SpatialCapture point) =>
        point switch
        {
            SeasonZone zone => RaidEditorSession.Copy(zone),
            MapVolume volume => RaidEditorSession.Copy(volume),
            MapObjectEdit edit => RaidEditorSession.Copy(edit),
            MapDoorEdit door => RaidEditorSession.Copy(door),
            MapLootPlacement loot => RaidEditorSession.Copy(loot),
            _ => RaidEditorSession.Copy(point),
        };

    private void RestorePoint(SpatialCapture target, SpatialCapture source)
    {
        target.Position = RaidEditorSession.Copy(source.Position);
        target.Rotation = RaidEditorSession.Copy(source.Rotation);
        if (target is MapVolume volume && source is MapVolume originalVolume)
        {
            volume.Size = RaidEditorSession.Copy(originalVolume.Size);
            volume.Radius = originalVolume.Radius;
        }
        if (target is MapObjectEdit obj && source is MapObjectEdit originalObject)
            obj.Scale = RaidEditorSession.Copy(originalObject.Scale);
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
        var after = CopyPoint(point);
        RestorePoint(point, _drag.Before);
        var transient = _drag.Transient;
        _drag = null;
        if (transient)
            _mapScene?.Reconcile(Layout);
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
        using var diagnostic = EditorDiagnostics.Measure(EditorDiagnostics.Area.Geometry);
        if (!_open || _session?.Definition == null)
        {
            _view?.HideRoute();
            return;
        }

        _lineIndex = 0;
        foreach (
            var zone in FilterZonesForLayout(_layoutId)
                .AsValueEnumerable()
                .OrderBy(z => z.Id == _selected ? 0 : 1)
                .ThenBy(z => Vector3.Distance(_flyPosition, ZoneRuntime.Vector(z.Position)))
                .Take(100)
        )
        {
            var color =
                zone.Id == _selected ? new Color(.85f, .78f, .45f)
                : zone.Hazard != null ? new Color(1f, .3f, .15f, .85f)
                : new Color(.4f, .7f, .6f, .7f);
            var center = ZoneRuntime.Vector(zone.Position);
            var rotation = Quaternion.Euler(ZoneRuntime.Vector(zone.Rotation));
            if (zone.Hazard?.Kind == "Claymore")
                Line(new[] { center, center + rotation * Vector3.forward * zone.Size.Z }, color);
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
        _view?.DrawRoute(
            (_mode == "Routes" || _mode == "AI") && !_walking ? Layout : null,
            _camera,
            _selected,
            _session?.ContentVersion ?? 0
        );
        DrawSelectionBounds();
        if (
            Selected is { } selected
            && CanUseHandle(selected)
            && _camera
            && _camera!.WorldToScreenPoint(HandleOrigin(selected)).z > _camera.nearClipPlane
        )
        {
            var hover = _drag?.Axis ?? HoverHandle(selected);
            var center = HandleOrigin(selected);
            var length = HandleLength(selected);
            var pixel = WTT.Campaigns.UI.Controls.SceneHandleMath.MetresPerPixel(_camera, center);
            for (var axis = 0; axis < 3; axis++)
            {
                var color =
                    hover == axis ? Color.yellow
                    : axis == 0 ? Color.red
                    : axis == 1 ? Color.green
                    : Color.cyan;
                var end = center + HandleAxis(selected, axis) * length;
                Line(HandlePoints(selected, axis), color, pixel * (hover == axis ? 4 : 3), true);
                if (_tool == "Rotate")
                    continue;
                // A camera-facing endpoint remains identifiable on large and tiny objects.
                var right = _camera.transform.right * pixel * 5;
                var up = _camera.transform.up * pixel * 5;
                Line(
                    new[] { end - right - up, end + right - up, end + right + up, end - right + up, end - right - up },
                    color,
                    pixel * 2,
                    true
                );
            }
        }

        for (var i = _lineIndex; i < _lines.Count; i++)
        {
            _lines[i].gameObject.SetActive(false);
        }
    }

    private void Line(Vector3[] points, Color color, float width = .025f, bool overlay = false)
    {
        if (!_lineMaterial)
        {
            _lineMaterial = WTT.Campaigns.UI.Controls.SceneHandleMath.LineMaterial(false);
            _handleMaterial = WTT.Campaigns.UI.Controls.SceneHandleMath.LineMaterial(true);
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
        renderer.sharedMaterial = overlay ? _handleMaterial : _lineMaterial;
        renderer.gameObject.SetActive(true);
        renderer.positionCount = points.Length;
        renderer.SetPositions(points);
        renderer.startColor = renderer.endColor = color;
        renderer.startWidth = renderer.endWidth = width;
    }

    private void ClearLines()
    {
        _view?.HideRoute();
        foreach (var line in _lines.AsValueEnumerable().Where(static l => l))
        {
            Destroy(line.gameObject);
        }

        _lines.Clear();
        if (_lineMaterial)
        {
            Destroy(_lineMaterial);
        }

        if (_handleMaterial)
            Destroy(_handleMaterial);
        _handleMaterial = null;
        _lineMaterial = null;
    }
}
