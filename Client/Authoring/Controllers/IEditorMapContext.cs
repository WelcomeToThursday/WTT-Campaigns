using UnityEngine;
using WTT.Campaigns.Client.Authoring.Scenes;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Client.Authoring.Controllers;

internal interface IEditorMapContext : IEditorDocumentContext
{
    Vector3 CameraPosition { get; set; }
    Quaternion CameraRotation { get; set; }
    string GhostRevision { get; set; }
    MapSceneAdapter? MapScene { get; set; }
    Transform? Picked { get; set; }
    string SceneSelectionError { get; set; }
    MapObjectEdit? SceneSelectionPose { get; set; }
    string SceneTab { get; set; }
    bool WalkFromStart { get; set; }
    bool Walking { get; }
    bool AiPreviewBusy { get; }
    bool CanSceneEdit { get; }
    SpatialCapture CapturePoint(string name);
    MapVolume CaptureVolume(string name);
    void EditPoint(Action<SpatialCapture> action);
    void EditTransformProperty(string field, Action<SpatialCapture> action);
    void EndWalkthrough(bool returnToEditor = false);
    bool MapWorkspace { get; }
    bool MissionContent { get; }
    void Number(string text, Action<float> action);
    void Open(bool requested = false);
    void ReconcileMapPreview();
    void RequestWalkthrough();
    SpatialCapture? ScenePoint { get; }
    bool SceneWorkspace { get; }
    bool TryDeleteLayoutZones(string layoutId, out string error);
}
