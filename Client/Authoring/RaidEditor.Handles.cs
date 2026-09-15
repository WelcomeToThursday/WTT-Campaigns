using UnityEngine;
using WTT.Campaigns.Client.Spatial;
using WTT.Campaigns.Shared.Spatial;
using WTT.Campaigns.UI.Controls;

namespace WTT.Campaigns.Client.Authoring;

public sealed partial class RaidEditor
{
    private bool _centerAnchor = true;
    private int _selectionBoundsFrame = -1;
    private Transform? _selectionBoundsTarget;
    private Bounds _selectionBounds;
    private bool _hasSelectionBounds;
    private Transform? SceneSelectionTarget =>
        _picked ? _picked
        : MapPoint != null ? _mapScene?.TargetFor(MapPoint.Id, MapPoint as MapObjectEdit)
        : null;

    private bool TrySelectionBounds(out Bounds bounds)
    {
        var target = SceneSelectionTarget;
        if (_selectionBoundsFrame != Time.frameCount || _selectionBoundsTarget != target)
        {
            _selectionBoundsFrame = Time.frameCount;
            _selectionBoundsTarget = target;
            _hasSelectionBounds = SceneBounds.TryGet(target, out _selectionBounds);
        }
        bounds = _selectionBounds;
        return _hasSelectionBounds;
    }

    private bool CanFrameScene =>
        SceneWorkspace
        && _sceneTab != "Catalog"
        && !_walking
        && _drag == null
        && _placementLifetime == null
        && _view?.Valid == true
        && !_view.Typing
        && !_view.Windows.HasMenu
        && _session?.Conflict == null
        && MapPoint is not MapObjectEdit { Operation: "Hide" }
        && TrySelectionBounds(out _);

    private void FrameSceneSelection()
    {
        if (!CanFrameScene || !_camera || !TrySelectionBounds(out var bounds))
            return;
        var halfAngle = Mathf.Atan(Mathf.Tan(_camera!.fieldOfView * Mathf.Deg2Rad / 2) * Mathf.Min(1, _camera.aspect));
        var distance = Mathf.Max(_camera.nearClipPlane + bounds.extents.magnitude, bounds.extents.magnitude / Mathf.Sin(halfAngle) * 1.25f);
        _flyPosition = bounds.center - _flyRotation * Vector3.forward * distance;
        _camera.transform.SetPositionAndRotation(_flyPosition, _flyRotation);
        DrawGeometry();
    }

    private Vector3 HandleOrigin(SpatialCapture point)
    {
        if (SceneWorkspace && _centerAnchor)
        {
            if (_drag != null && _tool != "Move")
                return _drag.Anchor;
            if (TrySelectionBounds(out var bounds))
                return bounds.center;
        }
        return ZoneRuntime.Vector(point.Position);
    }

    private bool CanUseHandle(SpatialCapture point) => !SceneWorkspace || CanTransformScene(_tool);

    private void KeepDragAnchor(SpatialCapture point, Drag drag)
    {
        if (!drag.Centered || _tool == "Move")
            return;
        var before = drag.Before;
        var scale =
            before is MapObjectEdit original ? ZoneRuntime.Vector(original.Scale)
            : drag.AnchorTarget ? drag.AnchorTarget!.lossyScale
            : Vector3.one;
        var nextScale = point is MapObjectEdit next ? ZoneRuntime.Vector(next.Scale) : scale;
        if (drag.AnchorTarget)
        {
            // RefreshMaps anchors the object after Unity applies its rotation and scale.
            point.Position = RaidEditorSession.Copy(before.Position);
            return;
        }
        point.Position = ZoneRuntime.Vector(
            SceneHandleMath.PositionAroundAnchor(
                ZoneRuntime.Vector(before.Position),
                Quaternion.Euler(ZoneRuntime.Vector(before.Rotation)),
                scale,
                Quaternion.Euler(ZoneRuntime.Vector(point.Rotation)),
                nextScale,
                drag.Anchor
            )
        );
    }

    private Vector3 HandleAxis(SpatialCapture point, int axis) =>
        _tool == "Scale" && point is MapObjectEdit ? Quaternion.Euler(ZoneRuntime.Vector(point.Rotation)) * Axis(axis) : Axis(axis);

    private Vector3[] HandlePoints(SpatialCapture point, int axis)
    {
        var center = HandleOrigin(point);
        var length = HandleLength(point);
        if (_tool != "Rotate")
            return new[] { center + HandleAxis(point, axis) * length * .15f, center + HandleAxis(point, axis) * length };
        var points = new Vector3[65];
        for (var i = 0; i < points.Length; i++)
        {
            var angle = i / 64f * Mathf.PI * 2;
            points[i] = center + (Axis((axis + 1) % 3) * Mathf.Cos(angle) + Axis((axis + 2) % 3) * Mathf.Sin(angle)) * length;
        }
        return points;
    }

    private Vector2 HoverTangent(SpatialCapture point, int axis)
    {
        SceneHandleMath.HitPath(_camera!, HandlePoints(point, axis), Input.mousePosition, out var tangent);
        return tangent;
    }

    private int HoverHandle(SpatialCapture point)
    {
        if (
            !_camera
            || !CanUseHandle(point)
            || UnityEngine.EventSystems.EventSystem.current?.IsPointerOverGameObject() == true
            || _view?.PointerOver == true
        )
            return -1;
        var best = -1;
        var nearest = 12f;
        for (var axis = 0; axis < 3; axis++)
        {
            var distance = SceneHandleMath.HitPath(_camera!, HandlePoints(point, axis), Input.mousePosition, out _);
            if (distance > nearest)
                continue;
            nearest = distance;
            best = axis;
        }
        return best;
    }

    private void DrawSelectionBounds()
    {
        if (!SceneWorkspace || _sceneTab == "Catalog" || !TrySelectionBounds(out var bounds))
            return;
        var corners = new Vector3[8];
        for (var i = 0; i < 8; i++)
            corners[i] =
                bounds.center
                + Vector3.Scale(bounds.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
        var width = _camera ? SceneHandleMath.MetresPerPixel(_camera!, bounds.center) * 1.5f : .02f;
        for (var i = 0; i < 8; i++)
        for (var axis = 0; axis < 3; axis++)
            if ((i & (1 << axis)) == 0)
                Line(new[] { corners[i], corners[i | (1 << axis)] }, new Color(.95f, .82f, .4f, .9f), width, true);
    }
}
