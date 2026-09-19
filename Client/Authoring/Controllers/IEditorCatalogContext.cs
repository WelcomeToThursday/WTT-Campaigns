using EFT.Interactive;
using UnityEngine;
using WTT.Campaigns.Client.Authoring.Scenes;
using WTT.Campaigns.Client.Authoring.Views;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Client.Authoring.Controllers;

internal interface IEditorCatalogContext : IEditorDocumentContext
{
    Camera? Camera { get; }
    bool CenterAnchor { get; set; }
    MapSceneAdapter? MapScene { get; set; }
    bool IsOpen { get; }
    int Page { get; set; }
    Transform? Picked { get; set; }
    bool Picking { get; set; }
    void ClearSceneSelection();
    string SceneSelectionError { get; set; }
    MapObjectEdit? SceneSelectionPose { get; set; }
    bool Snap { get; }
    string TransformTool { get; set; }
    bool WalkAssetsLoading { get; }
    bool Walking { get; }
    void BindDoorControls(RaidEditorView view);
    void CancelDrag();
    bool CanFrameScene { get; }
    bool CanTransformScene(string tool);
    void CaptureMapObject(string operation);
    void DeleteMapRecord();
    void FrameSceneSelection();
    bool IsDragging { get; }
    MapDoorEdit? MapDoor { get; }
    void MapEdit(Action<MapLayout> edit);
    SpatialCapture? MapPoint { get; }
    Door? PickedDoor { get; }
    void PresentDoorControls();
    void PresentPickedProperties();
    SpatialCapture? ScenePoint { get; }
    void SelectRow(string id);
    void SetSceneSelectionPose(Transform target, MapTarget? binding, string error);
}
