using UnityEngine;
using WTT.Campaigns.Client.Authoring.Console;
using WTT.Campaigns.Client.Authoring.Controllers;
using WTT.Campaigns.Client.Authoring.Scenes;
using WTT.Campaigns.Client.Spatial;
using WTT.Campaigns.Shared.Spatial;
using ZLinq;

namespace WTT.Campaigns.Client.Authoring;

public sealed partial class RaidEditor
{
    private string _layoutId = "",
        _ghostRevision = "";
    private bool _walking,
        _walkFromStart = true;
    private int _checkpoint;
    private Vector3? _returnPosition;
    private Vector2 _returnFacing;
    private Vector3? _walkCameraPosition;
    private Quaternion _walkCameraRotation;
    private string _walkCameraRaid = "";

    private void RestoreWalkCamera()
    {
        if (_walkCameraPosition.HasValue && _walkCameraRaid == _session?.RaidId)
        {
            _flyPosition = _walkCameraPosition.Value;
            _flyRotation = _walkCameraRotation;
        }
        _walkCameraPosition = null;
        _walkCameraRaid = "";
    }

    private MapSceneAdapter? _mapScene;
    private MapLayout? _walkLayout;
    private bool MapWorkspace => _mode == "Layouts" || _mode == "Routes";
    private MapLayout? Layout => _session?.Definition?.MapLayouts.AsValueEnumerable().FirstOrDefault(l => l.Id == _layoutId);

    private SpatialCapture CapturePoint(string name)
    {
        // The player stays at the raid spawn while the author flies the editor camera.
        // Capture the floor directly below that camera, never the distant player body.
        if (!_open || !_camera || !TryRouteFloor(_flyPosition, 20, out var floor))
            throw new InvalidOperationException("Move the editor camera above a floor within 20 metres to place a marker.");
        return new()
        {
            Id = EditorMapRecords.NewId(),
            Name = name,
            Location = _session!.Location,
            Scene = floor.transform.gameObject.scene.name,
            Position = ZoneRuntime.Vector(floor.point),
            Rotation = new SpatialVector { Y = _flyRotation.eulerAngles.y },
        };
    }

    private static bool TryRouteFloor(Vector3 origin, float distance, out RaycastHit floor)
    {
        floor = Physics
            .RaycastAll(
                origin,
                Vector3.down,
                distance,
                Physics.DefaultRaycastLayers & ~(1 << LayerMask.NameToLayer("Triggers")),
                QueryTriggerInteraction.Ignore
            )
            .AsValueEnumerable()
            .Where(hit => hit.collider && !hit.collider.GetComponentInParent<EFT.Player>())
            .OrderBy(hit => hit.distance)
            .FirstOrDefault();
        return floor.collider && floor.normal.y >= .5f;
    }

    private MapVolume CaptureVolume(string name)
    {
        var p = CapturePoint(name);
        return new MapVolume
        {
            Id = p.Id,
            Name = p.Name,
            Location = p.Location,
            Scene = p.Scene,
            Position = p.Position,
            Rotation = p.Rotation,
        };
    }

    internal void ReconcileMapPreview()
    {
        if (EditorMode.Ready && !_walking && _walkAssetLifetime == null)
        {
            _mapScene ??= new();
            _mapScene.Reconcile(Layout, _drag?.Transient == true ? _sceneSelectionPose : null);
            if (
                _drag is { Centered: true } drag
                && drag.AnchorTarget
                && _tool != "Move"
                && Selected is { } dragged
                && _mapScene.TargetErrors.Count == 0
            )
            {
                var position = WTT.Campaigns.UI.Controls.SceneSelectionGeometry.PositionForAnchor(
                    drag.AnchorTarget!,
                    drag.LocalAnchor,
                    drag.Anchor
                );
                if (drag.AnchorTarget!.position != position)
                {
                    drag.AnchorTarget.position = position;
                    _mapScene.RefreshVisuals(drag.AnchorTarget);
                }
                dragged.Position = ZoneRuntime.Vector(position);
            }
            _selectionBoundsFrame = -1;
            if (Catalog.SceneWorkspace && _picked && Maps.MapPoint == null && _sceneSelectionPose == null)
            {
                var error = MapSceneAdapter.Supported(_picked);
                SetSceneSelectionPose(_picked!, error.Length == 0 ? _mapScene.CaptureOriginal(_picked!) : null, error);
            }
        }
    }

    private static bool ClearPosition(Vector3 position, EFT.Player player)
    {
        var colliders = Physics.OverlapCapsule(
            position + Vector3.up * .4f,
            position + Vector3.up * 1.45f,
            .3f,
            Physics.DefaultRaycastLayers & ~(1 << LayerMask.NameToLayer("Triggers")),
            QueryTriggerInteraction.Ignore
        );
        return !colliders.AsValueEnumerable().Any(c => c && c.GetComponentInParent<EFT.Player>() != player)
            && TryRouteFloor(position + Vector3.up * .15f, 1.5f, out _);
    }

    private bool _walkRequested;

    private void RequestWalkthrough()
    {
        if (_session?.Busy == true || _session?.Dirty == true)
        {
            _walkRequested = true;
            ReportFeedback("Starting walkthrough after synchronization…");
            Refresh(false);
            return;
        }
        BeginWalkthrough();
    }

    private async void BeginWalkthrough()
    {
        if (_navigationPaint?.Busy == true || _navigationPaint?.Active == true)
        {
            ReportFeedback("Clear the manual navigation preview before starting a walkthrough.");
            return;
        }
        if (
            !EditorMode.Ready
            || _walking
            || _walkAssetLifetime != null
            || AiPreviewBusy
            || Layout == null
            || _session?.Conflict != null
            || _session?.Busy == true
            || _session?.Dirty == true
        )
            return;
        _mapScene ??= new();
        _walkAssetLifetime?.Cancel();
        _walkAssetLifetime = new CancellationTokenSource();
        var walkLifetime = _walkAssetLifetime;
        _session!.Previewing = _session.Hold = true;
        ReportFeedback("Preparing walkthrough scenery and containers�");
        try
        {
            _walkLayout = RaidEditorSession.Copy(Layout);
            if (!MissionContent)
            {
                _walkLayout.Start = null;
                _walkLayout.Checkpoints.Clear();
                _walkLayout.Encounters.Clear();
                _walkLayout.SpawnPoints.Clear();
                _walkLayout.PatrolRoutes.Clear();
            }
            await _mapScene.ApplyAsync(_walkLayout, MissionContent, walkLifetime.Token, runtime: true);
            _walkAssetLoot = new WTT.Campaigns.Client.Missions.MissionLoot();
            await _walkAssetLoot.ApplyAsync(_walkLayout, Guid.NewGuid().ToString("N"), walkLifetime.Token);
            walkLifetime.Token.ThrowIfCancellationRequested();
            Physics.SyncTransforms();
            Vector3? destination = null;
            if (MissionContent && _walkFromStart)
            {
                destination = ZoneRuntime.Vector(_walkLayout.Start!.Position);
                if (!ClearPosition(destination.Value, _player!))
                    throw new InvalidOperationException("The start marker needs clear standing room and a floor.");
                _returnPosition = _player!.Transform.position;
                _returnFacing = _player.Rotation;
            }
            _walking = true;
            _checkpoint = 0;
            _session!.Previewing = _session.Hold = true;
            _walkCameraPosition = _flyPosition;
            _walkCameraRotation = _flyRotation;
            _walkCameraRaid = _session.RaidId;
            Close();
            // Restore the native camera before teleporting; Close restores its saved world pose.
            if (destination.HasValue)
            {
                _player!.Teleport(destination.Value);
                _player.Rotation = new Vector2(_walkLayout.Start!.Rotation.Y, _walkLayout.Start.Rotation.X);
            }
            _view!.SetVisible(true);
            _view.Windows.SetWalkthrough(true);
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
        }
        catch (Exception e)
        {
            if (ReferenceEquals(_walkAssetLifetime, walkLifetime))
            {
                EndWalkthrough();
                ReportFeedback(e.Message, ConsoleSeverity.Error);
                Refresh(false);
            }
        }
    }

    private CancellationTokenSource? _walkAssetLifetime;
    private WTT.Campaigns.Client.Missions.MissionLoot? _walkAssetLoot;

    private bool UpdateWalkthrough()
    {
        if (!_walking)
            return false;
        if (
            !EditorMode.Ready
            || _session?.Grant.Length == 0
            || _session?.Conflict != null
            || !_player
            || Input.GetKeyDown(KeyCode.Escape)
            || _shortcut.Value.IsDown()
        )
        {
            EndWalkthrough(returnToEditor: true);
            return true;
        }
        var route = _walkLayout!;
        var target = _checkpoint < route.Checkpoints.Count ? route.Checkpoints[_checkpoint] : route.Exit;
        if (target != null && _checkpoint <= route.Checkpoints.Count && Inside(ToZone(target), _player!.Transform.position))
            _checkpoint++;
        if (_view?.Valid == true)
            _view.Text("EditorWalkStatus", PlayerRoute.Progress(route, _checkpoint) + " · Esc to return to editing");
        return false;
    }

    internal void EndWalkthrough(bool returnToEditor = false)
    {
        _walkAssetLifetime?.Cancel();
        _walkAssetLifetime?.Dispose();
        _walkAssetLifetime = null;
        _walkAssetLoot?.Dispose();
        _walkAssetLoot = null;
        if (AiPreviewBusy)
            EndAiPreview(false);
        var transition = returnToEditor ? System.Diagnostics.Stopwatch.StartNew() : null;
        try
        {
            _mapScene?.Dispose();
        }
        finally
        {
            _mapScene = null;
            _ghostRevision = "";
            _walking = false;
            _walkLayout = null;
            if (_returnPosition.HasValue && _player)
            {
                Physics.SyncTransforms();
                if (ClearPosition(_returnPosition.Value, _player!))
                {
                    _player!.Teleport(_returnPosition.Value);
                    _player.Rotation = _returnFacing;
                }
                else
                    ReportFeedback("Preview restored. Return position is obstructed; player remains here.");
            }
            _returnPosition = null;
            if (_session != null)
                _session.Previewing = _session.Hold = _aiCleanupFailed;
            if (_view?.Valid == true)
                _view.Windows.SetWalkthrough(false);
        }
        // Reclaim the camera before this frame renders. Update consumes the exit
        // input, so waiting for automatic opening exposes the native player pose.
        // Teardown callers leave returnToEditor false and must never reopen UI.
        if (returnToEditor && EditorMode.Ready && AuthoringEnabled && _session?.Definition != null)
        {
            var cleanupMilliseconds = transition!.Elapsed.TotalMilliseconds;
            Open();
            Plugin.LogInfo(
                $"Walkthrough return: cleanup {cleanupMilliseconds:0.0} ms, editor reopening "
                    + $"{transition.Elapsed.TotalMilliseconds - cleanupMilliseconds:0.0} ms."
            );
        }
    }

    private static SeasonZone ToZone(MapVolume v) =>
        new()
        {
            Id = v.Id,
            Position = v.Position,
            Rotation = v.Rotation,
            Size = v.Size,
            Radius = v.Radius,
            Shape = v.Shape,
        };
}
