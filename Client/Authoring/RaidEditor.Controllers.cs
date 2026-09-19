using EFT.Interactive;
using UnityEngine;
using WTT.Campaigns.Client.Authoring.Console;
using WTT.Campaigns.Client.Authoring.Controllers;
using WTT.Campaigns.Client.Authoring.Scenes;
using WTT.Campaigns.Client.Authoring.Views;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Client.Authoring;

public sealed partial class RaidEditor : IEditorAiContext, IEditorCatalogContext, IEditorMapContext
{
    bool IEditorAiContext.TryRouteFloor(Vector3 origin, float distance, out RaycastHit floor) => TryRouteFloor(origin, distance, out floor);

    private EditorAiController? _aiController;
    private EditorAiController Ai => _aiController ??= new(this);
    private EditorCatalogController? _catalogController;
    private EditorCatalogController Catalog => _catalogController ??= new(this);
    private EditorMapController? _mapController;
    private EditorMapController Maps => _mapController ??= new(this);
    string IEditorDocumentContext.LayoutId
    {
        get => _layoutId;
        set => _layoutId = value;
    }
    string IEditorDocumentContext.LibraryKey
    {
        get => _libraryKey;
        set => _libraryKey = value;
    }
    string IEditorDocumentContext.ToolId
    {
        get => _mode;
    }

    void IEditorDocumentContext.ReportFeedback(string message, ConsoleSeverity severity) => ReportFeedback(message, severity);

    List<(string Id, string Label)> IEditorDocumentContext.Rows
    {
        get => _rows;
    }
    string IEditorDocumentContext.SelectionId
    {
        get => _selected;
        set => _selected = value;
    }
    RaidEditorSession? IEditorDocumentContext.Session
    {
        get => _session;
    }
    RaidEditorView? IEditorDocumentContext.View
    {
        get => _view;
    }
    MapLayout? IEditorDocumentContext.Layout
    {
        get => Layout;
    }

    void IEditorDocumentContext.Refresh() => Refresh();

    void IEditorDocumentContext.Refresh(bool geometry) => Refresh(geometry);

    bool IEditorAiContext.AiPreview
    {
        get => _aiPreview;
    }
    string IEditorAiContext.AiPreviewStatus
    {
        get => _aiPreviewStatus;
    }
    bool IEditorAiContext.AiRuntimeAvailable
    {
        get => _aiRuntime != null;
    }
    bool IEditorAiContext.AiUseProfileKit
    {
        get => _aiUseProfileKit;
        set => _aiUseProfileKit = value;
    }
    Camera? IEditorAiContext.Camera
    {
        get => _camera;
    }
    Vector3 IEditorAiContext.CameraPosition
    {
        get => _flyPosition;
        set => _flyPosition = value;
    }
    Quaternion IEditorAiContext.CameraRotation
    {
        get => _flyRotation;
        set => _flyRotation = value;
    }
    bool IEditorAiContext.IsOpen
    {
        get => _open;
    }
    bool IEditorAiContext.AiPreviewBusy
    {
        get => AiPreviewBusy;
    }

    void IEditorAiContext.BeginAiPreview(bool playtest) => BeginAiPreview(playtest);

    MapVolume IEditorAiContext.CaptureVolume(string name) => CaptureVolume(name);

    void IEditorAiContext.EndAiPreview() => EndAiPreview();

    void IEditorAiContext.RestorePoint(SpatialCapture target, SpatialCapture source) => RestorePoint(target, source);

    void IEditorAiContext.SimulateAiEntry(string encounterId) => SimulateAiEntry(encounterId);

    void IEditorAiContext.SimulateAiEvent(string eventId) => SimulateAiEvent(eventId);

    void IEditorAiContext.SimulateAiStart() => SimulateAiStart();

    void IEditorAiContext.TestEditorCheckpoints() => TestEditorCheckpoints();

    Camera? IEditorCatalogContext.Camera
    {
        get => _camera;
    }
    bool IEditorCatalogContext.CenterAnchor
    {
        get => _centerAnchor;
        set => _centerAnchor = value;
    }
    MapSceneAdapter? IEditorCatalogContext.MapScene
    {
        get => _mapScene;
        set => _mapScene = value;
    }
    bool IEditorCatalogContext.IsOpen
    {
        get => _open;
    }
    int IEditorCatalogContext.Page
    {
        get => _page;
        set => _page = value;
    }
    Transform? IEditorCatalogContext.Picked
    {
        get => _picked;
        set => _picked = value;
    }
    bool IEditorCatalogContext.Picking
    {
        get => _picking;
        set => _picking = value;
    }

    void IEditorCatalogContext.ClearSceneSelection()
    {
        _sceneRenderers.Clear();
        _sceneRestrictionLog.Clear();
        _sceneSelectionPose = null;
        _sceneSelectionError = "";
    }

    string IEditorCatalogContext.SceneSelectionError
    {
        get => _sceneSelectionError;
        set => _sceneSelectionError = value;
    }
    MapObjectEdit? IEditorCatalogContext.SceneSelectionPose
    {
        get => _sceneSelectionPose;
        set => _sceneSelectionPose = value;
    }
    bool IEditorCatalogContext.Snap
    {
        get => _snap;
    }
    string IEditorCatalogContext.TransformTool
    {
        get => _tool;
        set => _tool = value;
    }
    bool IEditorCatalogContext.WalkAssetsLoading
    {
        get => _walkAssetLifetime != null;
    }
    bool IEditorCatalogContext.Walking
    {
        get => _walking;
    }

    void IEditorCatalogContext.BindDoorControls(RaidEditorView view) => Maps.BindDoorControls(view);

    void IEditorCatalogContext.CancelDrag() => CancelDrag();

    bool IEditorCatalogContext.CanFrameScene
    {
        get => CanFrameScene;
    }

    bool IEditorCatalogContext.CanTransformScene(string tool) => CanTransformScene(tool);

    void IEditorCatalogContext.CaptureMapObject(string operation) => Maps.CaptureMapObject(operation);

    void IEditorCatalogContext.DeleteMapRecord() => Maps.DeleteMapRecord();

    void IEditorCatalogContext.FrameSceneSelection() => FrameSceneSelection();

    bool IEditorCatalogContext.IsDragging
    {
        get => IsDragging;
    }
    MapDoorEdit? IEditorCatalogContext.MapDoor
    {
        get => Maps.MapDoor;
    }

    void IEditorCatalogContext.MapEdit(Action<MapLayout> edit) => Maps.MapEdit(edit);

    SpatialCapture? IEditorCatalogContext.MapPoint
    {
        get => Maps.MapPoint;
    }
    Door? IEditorCatalogContext.PickedDoor
    {
        get => Maps.PickedDoor;
    }

    void IEditorCatalogContext.PresentDoorControls() => Maps.PresentDoorControls();

    void IEditorCatalogContext.PresentPickedProperties() => PresentPickedProperties();

    SpatialCapture? IEditorCatalogContext.ScenePoint
    {
        get => ScenePoint;
    }

    void IEditorCatalogContext.SelectRow(string id) => SelectRow(id);

    void IEditorCatalogContext.SetSceneSelectionPose(Transform target, MapTarget? binding, string error) =>
        SetSceneSelectionPose(target, binding, error);

    Vector3 IEditorMapContext.CameraPosition
    {
        get => _flyPosition;
        set => _flyPosition = value;
    }
    Quaternion IEditorMapContext.CameraRotation
    {
        get => _flyRotation;
        set => _flyRotation = value;
    }
    string IEditorMapContext.GhostRevision
    {
        get => _ghostRevision;
        set => _ghostRevision = value;
    }
    MapSceneAdapter? IEditorMapContext.MapScene
    {
        get => _mapScene;
        set => _mapScene = value;
    }
    Transform? IEditorMapContext.Picked
    {
        get => _picked;
        set => _picked = value;
    }
    string IEditorMapContext.SceneSelectionError
    {
        get => _sceneSelectionError;
        set => _sceneSelectionError = value;
    }
    MapObjectEdit? IEditorMapContext.SceneSelectionPose
    {
        get => _sceneSelectionPose;
        set => _sceneSelectionPose = value;
    }
    string IEditorMapContext.SceneTab
    {
        get => Catalog.SceneTab;
        set => Catalog.SceneTab = value;
    }
    bool IEditorMapContext.WalkFromStart
    {
        get => _walkFromStart;
        set => _walkFromStart = value;
    }
    bool IEditorMapContext.Walking
    {
        get => _walking;
    }
    bool IEditorMapContext.AiPreviewBusy
    {
        get => AiPreviewBusy;
    }
    bool IEditorMapContext.CanSceneEdit
    {
        get => Catalog.CanSceneEdit;
    }

    SpatialCapture IEditorMapContext.CapturePoint(string name) => CapturePoint(name);

    MapVolume IEditorMapContext.CaptureVolume(string name) => CaptureVolume(name);

    void IEditorMapContext.EditPoint(Action<SpatialCapture> action) => EditPoint(action);

    void IEditorMapContext.EditTransformProperty(string field, Action<SpatialCapture> action) => EditTransformProperty(field, action);

    void IEditorMapContext.EndWalkthrough(bool returnToEditor) => EndWalkthrough(returnToEditor);

    bool IEditorMapContext.MapWorkspace
    {
        get => MapWorkspace;
    }
    bool IEditorMapContext.MissionContent
    {
        get => MissionContent;
    }

    void IEditorMapContext.Number(string text, Action<float> action) => Number(text, action);

    void IEditorMapContext.Open(bool requested) => Open(requested);

    void IEditorMapContext.ReconcileMapPreview() => ReconcileMapPreview();

    void IEditorMapContext.RequestWalkthrough() => RequestWalkthrough();

    SpatialCapture? IEditorMapContext.ScenePoint
    {
        get => ScenePoint;
    }
    bool IEditorMapContext.SceneWorkspace
    {
        get => Catalog.SceneWorkspace;
    }

    bool IEditorMapContext.TryDeleteLayoutZones(string layoutId, out string error) => TryDeleteLayoutZones(layoutId, out error);
}
