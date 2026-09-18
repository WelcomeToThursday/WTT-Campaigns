using EFT.UI.Screens;
using UnityEngine;
using WTT.Campaigns.Client.Authoring.Preview;
using WTT.Campaigns.Client.Encounters;
using WTT.Campaigns.Client.Missions;
using WTT.Campaigns.Client.Spatial;
using WTT.Campaigns.Shared.Spatial;
using ZLinq;

namespace WTT.Campaigns.Client.Authoring;

public sealed partial class RaidEditor
{
    private EncounterPreviewRuntime? _aiRuntime;
    private CancellationTokenSource? _aiLifetime;
    private EditorPreviewPlayer? _aiPlayer;
    private MissionLoot? _aiLoot;
    private readonly List<GameObject> _playtestHazards = new();
    private Task? _aiReset;
    private bool _aiPreview,
        _aiPlaytest;
    private string _aiPreviewStatus = "";
    private bool _aiPreparing,
        _aiDefeatPending,
        _aiCleanupFailed;
    private bool? _aiRequested;
    internal bool AiPreviewBusy =>
        _checkpointTestPreparing || _aiPreparing || _aiPreview || _aiCleanupFailed || _aiReset is { IsCompleted: false };
    internal static bool AiPreviewActive => Instance && Instance!._aiPreview;
    internal static bool AiPlaytestActive => Instance && Instance!._aiPreview && Instance._aiPlaytest && !Instance._aiDefeatPending;

    internal void AiDefeated() => _aiDefeatPending = true;

    private bool _aiUseProfileKit;

    private async void BeginAiPreview(bool playtest)
    {
        if (AiPreviewBusy || _walking || !EditorMode.Ready || !_open || Layout == null || _session?.Conflict != null)
            return;
        if (_session!.Busy || _session.Dirty)
        {
            _aiRequested = playtest;
            _notice = "Starting AI preview after the draft synchronizes…";
            return;
        }
        var session = _session;
        var player = _player!;
        var lifetime = _aiLifetime = new CancellationTokenSource();
        var transitionTimer = System.Diagnostics.Stopwatch.StartNew();
        var transitionStage = "scene preparation";
        _aiPreparing = true;
        _aiPreviewStatus = "Preparing AI preview…";
        _notice = "";
        session.Previewing = session.Hold = true;
        _aiDefeatPending = false;
        _aiPlaytest = false;
        try
        {
            if (player.IsInventoryOpened)
                throw new InvalidOperationException("Close the native inventory before starting an AI preview.");
            var compatibility = EncounterPreviewRuntime.CompatibilityError;
            if (compatibility.Length > 0)
                throw new InvalidOperationException(compatibility);
            // Mission rehearsals use the server-frozen descriptor returned by
            // prepare. Ordinary AI preview continues to use the live editor
            // session definition.
            var layout =
                _editorMissionRequested && _editorMissionLayout != null
                    ? RaidEditorSession.Copy(_editorMissionLayout)
                    : RaidEditorSession.Copy(Layout);
            if (_editorMissionRequested && !_editorMissionUseEncounters)
                layout.Encounters.Clear();
            _mapScene ??= new();
            _notice = _aiPreviewStatus = _editorMissionRequested
                ? "Preparing mission test · loading item models…"
                : "Preparing AI preview · loading item models…";
            if (_view?.Valid == true)
                Refresh(false);
            await _mapScene.ApplyAsync(layout, false, lifetime.Token);
            Plugin.LogInfo($"Preview transition: {transitionStage} {transitionTimer.ElapsedMilliseconds} ms");
            transitionTimer.Restart();
            transitionStage = "validation and equipment";
            lifetime.Token.ThrowIfCancellationRequested();
            if (_session != session || !EditorMode.Ready || !player)
                throw new OperationCanceledException();
            Physics.SyncTransforms();
            var errors = MapEncounterRules.Errors(layout, new EncounterNavigation(), true, true);
            if (errors.Count > 0)
                throw new InvalidOperationException(string.Join("\n", errors));
            if (playtest && (layout.Start == null || !ClearPosition(ZoneRuntime.Vector(layout.Start.Position), player)))
                throw new InvalidOperationException("Place a player start with clear standing room before playtesting.");
            _returnPosition = player.Transform.position;
            _returnFacing = player.Rotation;
            _walkCameraPosition = _flyPosition;
            _walkCameraRotation = _flyRotation;
            _walkCameraRaid = session.RaidId;
            if (playtest)
            {
                _aiPlayer = new EditorPreviewPlayer(player);
                await _aiPlayer.Equip(
                    lifetime.Token,
                    _aiUseProfileKit,
                    stage =>
                    {
                        if (_aiLifetime != lifetime || lifetime.IsCancellationRequested || _session != session)
                            return;
                        _notice = _aiPreviewStatus = "Preparing playtest · " + stage;
                        if (_view?.Valid == true)
                            Refresh(false);
                    }
                );
            }
            lifetime.Token.ThrowIfCancellationRequested();
            if (_session != session || !EditorMode.Ready || !player)
                throw new OperationCanceledException();
            _aiRuntime = new EncounterPreviewRuntime();
            Plugin.LogInfo($"Preview transition: {transitionStage} {transitionTimer.ElapsedMilliseconds} ms");
            transitionTimer.Restart();
            transitionStage = "native encounter preparation";
            await _aiRuntime.BeginAsync(
                new EncounterRuntimeContext
                {
                    SessionId = EditorMode.SessionId,
                    RaidId = session.RaidId,
                    LayoutId = layout.Id,
                    LayoutRevision = session.ContentVersion,
                    Mode = EncounterRuntimeModes.Preview,
                    PreviewGeneration = Guid.NewGuid().ToString("N"),
                },
                layout,
                player,
                !playtest,
                lifetime.Token
            );
            lifetime.Token.ThrowIfCancellationRequested();
            if (playtest)
            {
                Close();
                player.Teleport(ZoneRuntime.Vector(layout.Start!.Position));
                player.Rotation = new Vector2(layout.Start.Rotation.Y, layout.Start.Rotation.X);
                _aiPlayer?.Arm();
                _view!.SetVisible(true);
                _view.Windows.SetWalkthrough(true);
                Cursor.visible = false;
                Cursor.lockState = CursorLockMode.Locked;
            }
            _aiPreview = true;
            _aiPlaytest = playtest;
            _aiPreviewStatus = playtest ? "Combat playtest · Esc to reset" : "Observe · simulate an event or mission start";
            Plugin.LogInfo($"Preview transition: {transitionStage} {transitionTimer.ElapsedMilliseconds} ms");
            transitionTimer.Restart();
            transitionStage = "mission route and loot";
            if (playtest && !_editorMissionRequested)
            {
                // Replace inert editor models with native, collectable loot.
                await _mapScene.ApplyAsync(layout, false, lifetime.Token, runtime: true);
                _aiLoot = new MissionLoot();
                await _aiLoot.ApplyAsync(layout, Guid.NewGuid().ToString("N"), lifetime.Token);
                lifetime.Token.ThrowIfCancellationRequested();
            }
            await BeginEditorMissionRoute(layout, lifetime.Token);
            if (playtest)
                await BeginPlaytestHazards(
                    _editorMissionRequested && _editorMissionTest != null
                        ? _editorMissionTest.Descriptor.Zones
                        : FilterZonesForLayout(layout.Id),
                    lifetime.Token
                );
            await CaptureTestStart(lifetime.Token);
            _aiRuntime.MissionStart();
            _editorDirector?.Observe(
                new WTT.Campaigns.Shared.Missions.MissionSignal { Kind = WTT.Campaigns.Shared.Missions.MissionSignals.Start }
            );
            _notice = "";
        }
        catch (OperationCanceledException)
        {
            EndAiPreview(false);
        }
        catch (Exception error)
        {
            _notice = (_editorMissionRequested ? "Mission test unavailable: " : "AI preview unavailable: ") + error.Message;
            Plugin.Error(error);
            EndAiPreview();
        }
        finally
        {
            Plugin.LogInfo($"Preview transition finished: {transitionStage} {transitionTimer.ElapsedMilliseconds} ms");
            _aiPreparing = false;
            if (_aiLifetime == lifetime && !_aiPreview)
                session.Previewing = session.Hold = _aiCleanupFailed;
            if (_view?.Valid == true)
                Refresh(false);
        }
    }

    private void EndAiPreview() => EndAiPreview(true);

    private void EndAiPreview(bool reopen)
    {
        _aiRequested = null;
        // Close through the native screen lifecycle before destroying loot or
        // replacing equipment that an inventory/loot screen may still observe.
        if (_player && _player!.IsInventoryOpened)
            EftScreenManager.Instance.ToggleScreen(EEftScreenType.Inventory);
        EndPlaytestHazards();
        EndEditorMissionRoute(_editorMissionRetrying);
        if (!AiPreviewBusy && _aiRuntime == null && _aiPlayer == null)
            return;
        _aiLifetime?.Cancel();
        _aiPlaytest = _aiPreview = false;
        _aiDefeatPending = false;
        _aiPreviewStatus = _notice;
        try
        {
            _aiRuntime?.Reset();
            _aiRuntime = null;
        }
        catch (Exception error)
        {
            _aiCleanupFailed = true;
            _notice = _aiPreviewStatus = "Preview cleanup needs attention: " + error.GetBaseException().Message;
            Plugin.Error(error);
            return;
        }
        if (_aiReset is { IsCompleted: false })
            return;
        _aiReset = RestoreAiPreview(reopen);
    }

    private async Task RestoreAiPreview(bool reopen)
    {
        var transitionTimer = System.Diagnostics.Stopwatch.StartNew();
        var session = _session;
        var player = _player;
        var gear = _aiPlayer;
        var scene = _mapScene;
        var lifetime = _aiLifetime;
        var position = _returnPosition;
        var facing = _returnFacing;
        _aiPlayer = null;
        _mapScene = null;
        _aiLifetime = null;
        _returnPosition = null;
        try
        {
            _aiLoot?.Dispose();
            _aiLoot = null;
            scene?.Dispose();
            Plugin.LogInfo($"Preview reset: scene cleanup {transitionTimer.ElapsedMilliseconds} ms");
            transitionTimer.Restart();
            if (gear != null)
                await gear.Restore();
            Plugin.LogInfo($"Preview reset: equipment restoration {transitionTimer.ElapsedMilliseconds} ms");
            transitionTimer.Restart();
            if (_session == session)
                _aiCleanupFailed = false;
        }
        catch (Exception error)
        {
            if (_session == session)
            {
                _aiCleanupFailed = true;
                _aiPlayer = gear;
                _notice = _aiPreviewStatus = "Preview cleanup needs attention: " + error.GetBaseException().Message;
            }
            Plugin.Error(error);
        }
        finally
        {
            lifetime?.Dispose();
            if (session != null)
                session.Previewing = session.Hold = _session == session && _aiCleanupFailed;
            // Nothing from a retired session may restore over a new map or camera.
            if (_session == session && _player == player)
            {
                _ghostRevision = "";
                if (position.HasValue && player && ClearPosition(position.Value, player!))
                {
                    player!.Teleport(position.Value);
                    player.Rotation = facing;
                }
                if (_view?.Valid == true)
                    _view.Windows.SetWalkthrough(false);
                if (reopen && EditorMode.Ready && session?.Definition != null)
                {
                    if (!_open)
                        Open();
                    else
                        RestoreWalkCamera();
                    Refresh(false);
                }
            }
            Plugin.LogInfo($"Preview reset: editor restoration {transitionTimer.ElapsedMilliseconds} ms");
        }
    }

    private void SimulateAiEvent(string eventId)
    {
        if (!_aiPreview || _aiRuntime == null)
            return;
        _aiRuntime.Event(eventId);
    }

    private void SimulateAiStart()
    {
        if (_aiPreview)
            _aiRuntime?.MissionStart();
    }

    private void SimulateAiEntry(string encounterId)
    {
        if (_aiPreview)
            _aiRuntime?.SimulateEntry(encounterId);
    }

    private bool UpdateAiPreview()
    {
        if (!AiPreviewBusy)
            return false;
        if (HoldTestFailure())
            return true;
        if (_editorMissionCompleted && Input.GetKeyDown(KeyCode.R))
        {
            _ = RetryEditorMissionTest();
            return true;
        }
        if (
            _aiDefeatPending
            || !EditorMode.Ready
            || _session?.Conflict != null
            || Time.realtimeSinceStartup - _lastContact > 20
            || Input.GetKeyDown(KeyCode.Escape)
            || _shortcut.Value.IsDown()
        )
        {
            _notice = _aiDefeatPending ? "Playtest defeated. Preview reset." : "AI preview reset.";
            EndAiPreview();
            return true;
        }
        try
        {
            if (_aiPreview)
            {
                _aiRuntime?.Tick();
                _editorDirector?.Tick();
                if ((_editorDirector?.HasPending == true || _editorSignals != null) && !_editorMissionProgressPending)
                    _ = ReportEditorObservations();
                var failure = _aiRuntime?.Failure;
                if (!string.IsNullOrEmpty(failure))
                    throw new InvalidOperationException(failure);
            }
        }
        catch (Exception error)
        {
            _notice = "AI preview stopped: " + error.GetBaseException().Message;
            Plugin.Error(error);
            EndAiPreview();
            return true;
        }
        if (_aiRuntime != null && !_editorMissionRequested)
            _aiPreviewStatus = _aiRuntime.Status;
        if (_view?.Valid == true && _aiPlaytest)
            _view.Text("EditorWalkStatus", _aiPreviewStatus + " · Esc to return to editing");
        return false;
    }
}
