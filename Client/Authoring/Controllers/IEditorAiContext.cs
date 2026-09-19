using UnityEngine;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Client.Authoring.Controllers;

internal interface IEditorAiContext : IEditorDocumentContext
{
    bool AiPreview { get; }
    string AiPreviewStatus { get; }
    bool AiRuntimeAvailable { get; }
    bool AiUseProfileKit { get; set; }
    Camera? Camera { get; }
    Vector3 CameraPosition { get; set; }
    Quaternion CameraRotation { get; set; }
    bool IsOpen { get; }
    bool AiPreviewBusy { get; }
    void BeginAiPreview(bool playtest);
    MapVolume CaptureVolume(string name);
    void EndAiPreview();
    void RestorePoint(SpatialCapture target, SpatialCapture source);
    void SimulateAiEntry(string encounterId);
    void SimulateAiEvent(string eventId);
    void SimulateAiStart();
    void TestEditorCheckpoints();
    bool TryRouteFloor(Vector3 origin, float distance, out RaycastHit floor);
}
