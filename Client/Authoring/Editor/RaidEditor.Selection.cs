using UnityEngine;
using WTT.Campaigns.Client.Authoring.Controllers;
using WTT.Campaigns.Client.Authoring.Scenes;
using WTT.Campaigns.Client.Spatial;
using WTT.Campaigns.Shared.Spatial;
using WTT.Campaigns.UI.Controls;
using ZLinq;
using Button = WTT.Campaigns.Client.Authoring.Views.EditorButton;
using InputField = WTT.Campaigns.Client.Authoring.Views.EditorInput;

namespace WTT.Campaigns.Client.Authoring.Editor;

public sealed partial class RaidEditor
{
    private MapObjectEdit? _sceneSelectionPose;
    private string _sceneSelectionError = "";
    private readonly HashSet<string> _sceneRestrictionLog = new();
    private readonly List<Renderer> _sceneRenderers = new();
    private SpatialCapture? ScenePoint => Maps.MapPoint ?? (_picked ? _sceneSelectionPose : null);

    private bool CanTransformScene(string tool) =>
        Catalog.CanSceneEdit
        && Catalog.InspectingScene
        && ScenePoint != null
        && (ScenePoint is MapVolume || SceneSelectionTarget)
        && _sceneSelectionError.Length == 0
        && ScenePoint is not MapObjectEdit { Operation: "Hide" }
        && (
            tool != "Scale"
            || ScenePoint is MapVolume
            || ScenePoint is MapObjectEdit { Target.Kind: "Prop" or "AssetProp" }
                && SceneSelectionTarget
                && MapSceneAdapter.ScaleRestriction(SceneSelectionTarget!).Length == 0
        );

    private void SetSceneSelectionPose(Transform target, MapTarget? binding, string error)
    {
        _sceneSelectionError = error;
        if (error.Length > 0 && _sceneRestrictionLog.Count < 128 && _sceneRestrictionLog.Add(target.GetInstanceID() + ":" + error))
        {
            Plugin.LogInfo("Scene edit restriction: " + target.name + " | " + error + " | " + ScenePath(target));
            // Bound traversal even when an aggregate map branch was selected.
            var pending = new Stack<Transform>();
            var components = new HashSet<string>();
            pending.Push(target);
            var visited = 0;
            while (pending.Count > 0 && visited++ < 256)
            {
                var node = pending.Pop();
                foreach (var component in node.GetComponents<Component>())
                    components.Add(component ? component.GetType().FullName ?? component.GetType().Name : "<missing>");
                for (var child = 0; child < node.childCount && pending.Count < 256; child++)
                    pending.Push(node.GetChild(child));
            }
            Plugin.LogInfo(
                "Scene component inventory: "
                    + target.name
                    + " | "
                    + string.Join(", ", components)
                    + (pending.Count > 0 ? " | truncated" : "")
            );
        }
        _sceneSelectionPose = new MapObjectEdit
        {
            Id = EditorMapRecords.NewId(),
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
        Catalog.RememberSceneTarget(target);
        _libraryKey = "";
        _selectionBoundsFrame = -1;
    }

    private void CommitSceneSelection(Action<SpatialCapture> action)
    {
        if (!CanTransformScene("Move") || _sceneSelectionPose == null || Maps.MapPoint != null)
            return;
        var after = SceneSelectionEdit.Prepare(_sceneSelectionPose, action);
        if (after == null)
            return;
        Maps.MapEdit(layout =>
        {
            layout.Objects.Add(after);
            _selected = after.Id;
        });
        _sceneSelectionPose = null;
    }

    private void EditTransformProperty(string field, Action<SpatialCapture> action)
    {
        if (Catalog.SceneWorkspace && !CanTransformScene(field == "Size" ? "Scale" : "Move"))
            return;
        if (
            !Catalog.SceneWorkspace
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
                _mapScene!.Reconcile(Layout, Maps.MapPoint == null ? point as MapObjectEdit : null);
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
        var previous = ToolStateFor(_mode);
        previous.Selection = _selected;
        previous.Page = _page;
        previous.Picked = _picked;
        _mode = "Scene";
        if (_view != null)
        {
            _view.ToolContext = "Scene";
            _view.Windows.BrowseCategory();
        }
        _page = 0;
        _libraryKey = "";
    }

    private void PickScene()
    {
        if (!_camera)
            return;
        var target = ScenePicking.Pick(
            _camera!.EditorScreenPointToRay(Input.mousePosition),
            PickableRenderers(),
            t => _mapScene?.RecordAt(t) != null
        );
        if (target)
        {
            EnterSceneSelection();
            Catalog.SelectSceneTarget(target!);
        }
        else
        {
            _picked = null;
            _selected = "";
            _sceneSelectionPose = null;
            _sceneSelectionError = "";
            ReportFeedback("Click an object to select it.");
            Refresh();
        }
    }

    private void PresentPickedProperties()
    {
        if (!Catalog.SceneWorkspace || !Catalog.InspectingScene)
            return;
        var point = ScenePoint;
        if (point == null)
        {
            if (Maps.MapDoor is not { } door)
                return;
            var doorView = _view!;
            doorView.Value("MapName", door.Name);
            doorView.Get<InputField>("MapName").readOnly = !Catalog.CanSceneEdit;
            foreach (var group in new[] { "Position", "Rotation", "Size" })
            foreach (var axis in "XYZ")
            {
                doorView.Get<InputField>("Map" + group + axis).readOnly = true;
                doorView.Value("Map" + group + axis, "0");
            }
            doorView.Get<Button>("MapAtPlayer").interactable = false;
            doorView.Text(
                "MapDetails",
                "Door state: "
                    + door.State
                    + "\n"
                    + door.Target.Scene
                    + ":"
                    + door.Target.Path
                    + "\nUse Door state to cycle this layout's saved state."
            );
            return;
        }
        var view = _view!;
        var editable = CanTransformScene("Move");
        view.Value("MapName", point.Name);
        view.Get<InputField>("MapName").readOnly = !editable;
        foreach (var group in new[] { "Position", "Rotation", "Size" })
        {
            var v =
                group == "Position" ? point.Position
                : group == "Rotation" ? point.Rotation
                : point is MapVolume volume ? volume.Size
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
        else if (Maps.MapPoint == null)
            view.Text("SceneInfo", "Drag a handle or edit a property. Changes are saved only when you edit; Esc cancels a drag.");
        if (
            editable
            && target
            && point is MapObjectEdit { Target.Kind: "Prop" or "AssetProp", Operation: "Move" }
            && MapSceneAdapter.Supported(target, copy: true) is { Length: > 0 } copyReason
        )
            view.Text("SceneInfo", copyReason);
        if (editable && !CanTransformScene("Scale"))
            view.Text(
                "SceneInfo",
                "Move and rotate are available. "
                    + (
                        point is MapObjectEdit { Target.Kind: "Prop" or "AssetProp" } && target
                            ? MapSceneAdapter.ScaleRestriction(target!)
                            : "Native loot and containers retain their original size."
                    )
            );
        view.Windows.SetTooltip(
            "Scale",
            editable
            && point is MapObjectEdit { Target.Kind: "Prop" or "AssetProp" }
            && target
            && MapSceneAdapter.ScaleRestriction(target!) is { Length: > 0 } scaleReason
                ? scaleReason
                : "Resize a selected static prop or volume. Native loot and containers retain their original size."
        );
    }
}
