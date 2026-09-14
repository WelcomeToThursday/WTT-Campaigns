using UnityEngine;
using UnityEngine.UI;
using WTT.Campaigns.Client.Spatial;
using WTT.Campaigns.Shared.Spatial;
using WTT.Campaigns.UI.Controls;
using ZLinq;

namespace WTT.Campaigns.Client.Authoring;

public sealed partial class RaidEditor
{
    private MapObjectEdit? _sceneSelectionPose;
    private string _sceneSelectionError = "";
    private readonly HashSet<string> _sceneRestrictionLog = new();
    private readonly List<Renderer> _sceneRenderers = new();
    private SpatialCapture? ScenePoint => MapPoint ?? (_picked ? _sceneSelectionPose : null);

    private bool CanTransformScene(string tool) =>
        CanSceneEdit
        && _sceneTab != "Catalog"
        && ScenePoint != null
        && SceneSelectionTarget
        && _sceneSelectionError.Length == 0
        && ScenePoint is not MapObjectEdit { Operation: "Hide" }
        && (
            tool != "Scale"
            || ScenePoint is MapObjectEdit { Target.Kind: "Prop" } && MapSceneAdapter.ScaleRestriction(SceneSelectionTarget!).Length == 0
        );

    private void SetSceneSelectionPose(Transform target, MapTarget? binding, string error)
    {
        _sceneSelectionError = error;
        if (error.Length > 0 && _sceneRestrictionLog.Count < 128 && _sceneRestrictionLog.Add(target.GetInstanceID() + ":" + error))
            Plugin.LogInfo("Scene edit restriction: " + target.name + " | " + error + " | " + ScenePath(target));
        _sceneSelectionPose = new MapObjectEdit
        {
            Id = MapId(),
            Name = target.name,
            Location = _session!.Location,
            Scene = target.gameObject.scene.name,
            Position = ZoneRuntime.Vector(target.position),
            Rotation = ZoneRuntime.Vector(target.eulerAngles),
            Scale = ZoneRuntime.Vector(target.lossyScale),
            Target = binding ?? new MapTarget(),
            Operation = "Move",
        };
        if (_tool == "Scale" && !CanTransformScene("Scale"))
            _tool = "Move";
        _sceneRoots[target.GetInstanceID().ToString()] = target;
        _libraryKey = "";
        _selectionBoundsFrame = -1;
    }

    private void CommitSceneSelection(Action<SpatialCapture> action)
    {
        if (!CanTransformScene("Move") || _sceneSelectionPose == null || MapPoint != null)
            return;
        var after = SceneSelectionEdit.Prepare(_sceneSelectionPose, action);
        if (after == null)
            return;
        MapEdit(layout =>
        {
            layout.Objects.Add(after);
            _selected = after.Id;
        });
        _sceneSelectionPose = null;
    }

    private void EditTransformProperty(string field, Action<SpatialCapture> action)
    {
        if (SceneWorkspace && !CanTransformScene(field == "Size" ? "Scale" : "Move"))
            return;
        if (
            !SceneWorkspace
            || !_centerAnchor
            || field == "Position"
            || !CanTransformScene(field == "Size" ? "Scale" : "Rotate")
            || !TrySelectionBounds(out var bounds)
            || !SceneSelectionTarget
        )
        {
            EditPoint(action);
            return;
        }
        var target = SceneSelectionTarget!;
        var anchor = bounds.center;
        var localAnchor = target.InverseTransformPoint(anchor);
        try
        {
            EditPoint(point =>
            {
                var before = CopyPoint(point);
                action(point);
                if (
                    Newtonsoft.Json.Linq.JToken.DeepEquals(
                        Newtonsoft.Json.Linq.JObject.FromObject(before),
                        Newtonsoft.Json.Linq.JObject.FromObject(point)
                    )
                )
                    return;
                _mapScene!.Reconcile(Layout, MapPoint == null ? point as MapObjectEdit : null);
                if (_mapScene.TargetErrors.Count > 0)
                    throw new InvalidOperationException(_mapScene.TargetErrors[0]);
                point.Position = ZoneRuntime.Vector(SceneSelectionGeometry.PositionForAnchor(target, localAnchor, anchor));
            });
        }
        finally
        {
            // A failed first edit must restore its temporary preview as well as the draft.
            _mapScene?.Reconcile(Layout);
            _selectionBoundsFrame = -1;
        }
    }

    private IEnumerable<Renderer> PickableRenderers()
    {
        foreach (var renderer in _sceneRenderers)
            yield return renderer;
        if (_mapScene != null)
            foreach (var renderer in _mapScene.SpawnRenderers())
                yield return renderer;
    }

    private void EnterSceneSelection()
    {
        if (_mode == "Scene")
            return;
        _moduleSelection[_mode] = (_selected, _page);
        _mode = "Scene";
        _sceneTab = "Existing";
        _page = 0;
        _libraryKey = "";
    }

    private void PickScene()
    {
        if (!_camera)
            return;
        var target = ScenePicking.Pick(
            _camera!.ScreenPointToRay(Input.mousePosition),
            PickableRenderers(),
            t => _mapScene?.RecordAt(t) != null
        );
        if (target)
        {
            EnterSceneSelection();
            SelectSceneTarget(target!);
        }
        else
        {
            _picked = null;
            _selected = "";
            _sceneSelectionPose = null;
            _sceneSelectionError = "";
            _notice = "Click an object to select it.";
            Refresh();
        }
    }

    private void PresentPickedProperties()
    {
        if (!SceneWorkspace || _sceneTab == "Catalog")
            return;
        var point = ScenePoint;
        if (point == null)
            return;
        var view = _view!;
        var editable = CanTransformScene("Move");
        view.Value("MapName", point.Name);
        view.Get<InputField>("MapName").readOnly = !editable;
        foreach (var group in new[] { "Position", "Rotation", "Size" })
        {
            var v =
                group == "Position" ? point.Position
                : group == "Rotation" ? point.Rotation
                : (point as MapObjectEdit)?.Scale;
            for (var i = 0; i < 3; i++)
            {
                var name = "Map" + group + "XYZ"[i];
                view.Value(
                    name,
                    (
                        v == null ? 0
                        : i == 0 ? v.X
                        : i == 1 ? v.Y
                        : v.Z
                    ).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
                );
                view.Get<InputField>(name).readOnly = !editable || group == "Size" && !CanTransformScene("Scale");
            }
        }
        view.Get<Button>("MapAtPlayer").interactable = editable;
        var target = SceneSelectionTarget;
        view.Text(
            "MapDetails",
            target
                ? (ScenePath(target!) ?? target!.name)
                    + "\n"
                    + target!.GetComponents<Component>().AsValueEnumerable().Where(c => c).Select(c => c.GetType().Name).JoinToString(", ")
                : point.Scene
        );
        if (_sceneSelectionError.Length > 0)
            view.Text("SceneInfo", "Selected for inspection. " + _sceneSelectionError);
        else if (MapPoint == null)
            view.Text("SceneInfo", "Drag a handle or edit a property. Changes are saved only when you edit; Esc cancels a drag.");
        if (
            editable
            && target
            && point is MapObjectEdit { Target.Kind: "Prop", Operation: "Move" }
            && MapSceneAdapter.Supported(target, copy: true) is { Length: > 0 } copyReason
        )
            view.Text("SceneInfo", copyReason);
        if (editable && !CanTransformScene("Scale"))
            view.Text(
                "SceneInfo",
                "Move and rotate are available. "
                    + (
                        point is MapObjectEdit { Target.Kind: "Prop" } && target
                            ? MapSceneAdapter.ScaleRestriction(target!)
                            : "Native loot and containers retain their original size."
                    )
            );
        view.Windows.SetTooltip(
            "Scale",
            editable
            && point is MapObjectEdit { Target.Kind: "Prop" }
            && target
            && MapSceneAdapter.ScaleRestriction(target!) is { Length: > 0 } scaleReason
                ? scaleReason
                : "Resize a selected static prop or volume. Native loot and containers retain their original size."
        );
    }
}
