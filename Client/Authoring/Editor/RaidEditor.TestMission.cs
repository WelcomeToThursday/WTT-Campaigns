using Comfort.Common;
using EFT;
using UnityEngine;
using UnityEngine.SceneManagement;
using WTT.Campaigns.Client.Authoring.Console;
using WTT.Campaigns.Client.Missions;
using WTT.Campaigns.Client.Spatial;
using WTT.Campaigns.Shared.Authoring;
using WTT.Campaigns.Shared.Missions;
using WTT.Campaigns.Shared.Spatial;
using ZLinq;

namespace WTT.Campaigns.Client.Authoring.Editor;

public sealed partial class RaidEditor
{
    private sealed class EditorMissionRouteTrigger : MonoBehaviour, IPhysicsTriggerWithStay
    {
        private RaidEditor? _owner;
        private string _kind = "";
        private string _id = "";

        internal void Initialize(RaidEditor owner, string kind, string id)
        {
            _owner = owner;
            _kind = kind;
            _id = id;
        }

        public string Description => "Editor mission route";

        public void OnTriggerEnter(Collider other) => _owner?.EditorMissionRouteEntered(_kind, _id, other);

        public void OnTriggerExit(Collider other) { }

        void IPhysicsTriggerWithStay.OnTriggerStay(Collider other, Collider trigger) => OnTriggerEnter(other);

        private void OnTriggerStay(Collider other) => OnTriggerEnter(other);

        private void OnDestroy() => _owner = null;
    }

    private EditorTestMissionResponse? _editorMissionTest;
    private EditorTestMissionResponse? _editorMissionPending;
    private MapLayout? _editorMissionLayout;
    private MissionLoot? _editorMissionLoot;
    private MissionEnvironment? _testEnvironment;
    private readonly List<GameObject> _editorMissionVolumes = new();
    private CancellationTokenSource? _editorMissionLifetime;
    private long _editorMissionGeneration;
    private int _editorMissionCheckpoint;
    private bool _editorMissionRequested;
    private bool _editorMissionUseEncounters = true;
    private bool _editorMissionCompleted;
    private bool _editorMissionProgressPending;
    private bool _editorMissionRetrying;
    private MissionDirector? _editorDirector;
    private List<MissionSignal>? _editorSignals;
    private string _editorObservationOperation = "";
    private List<MissionActor> _editorObservationActors = new();

    private async Task ReportEditorObservations()
    {
        var director = _editorDirector;
        var test = _editorMissionTest;
        var lifetime = _editorMissionLifetime;
        if (
            director == null
            || test == null
            || lifetime == null
            || _editorMissionProgressPending
            || _editorMissionCompleted
            || test.Run is not { Status: MissionRunStatuses.Active, ExitReached: false }
        )
            return;
        _editorMissionProgressPending = true;
        try
        {
            if (_editorSignals == null)
            {
                _editorSignals = director.Take();
                _editorObservationActors = new(director.Actors);
                _editorObservationOperation = Guid.NewGuid().ToString("N");
            }
            var response = await EditorMissionTestClient.ObserveAsync(
                test,
                _editorSignals,
                _editorObservationActors,
                _editorObservationOperation,
                lifetime.Token
            );
            if (director != _editorDirector || lifetime.IsCancellationRequested)
                return;
            MissionAcknowledgement.Require(test.Run!, response.Run, response.Committed);
            _editorSignals = null;
            _editorMissionTest = response;
            if (response.Run != null)
            {
                director.Accept(response.Run.Logic);
                _testHud?.Accept(response.Run);
                CheckTestFailure();
                var progress = response.Run.Logic;
                _aiPreviewStatus =
                    progress.Failure.Length > 0
                        ? progress.Failure
                        : "Mission test · "
                            + string.Join(
                                " · ",
                                test.Descriptor!.Definition.Objectives.AsValueEnumerable()
                                    .Select(o => o.Name + ": " + MissionLogic.Progress(progress, o.Id).Status)
                                    .ToArray()
                            );
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            _aiPreviewStatus = "Mission observations: " + error.Message;
            Plugin.Error(error);
        }
        finally
        {
            if (director == _editorDirector)
                _editorMissionProgressPending = false;
        }
    }

    internal bool EditorMissionTestActive => _editorMissionRequested || _editorMissionPending != null;
    internal static bool MissionTestActive => Instance && Instance!.EditorMissionTestActive;

    private bool _checkpointTestPreparing;
    private string _checkpointTestLayout = "",
        _checkpointTestDraft = "";
    private float _checkpointTestDeadline;

    private void CheckpointTestMessage(string message)
    {
        ReportFeedback(message);
        Plugin.LogInfo(message);
        EFT.UI.ItemUiContext.Instance.ShowMessageWindow(message, null, null, "OK", 0f, forceShow: true);
    }

    private void TestEditorCheckpoints()
    {
        if (_session == null || Layout == null)
        {
            CheckpointTestMessage("Select a layout before testing checkpoints.");
            return;
        }
        _checkpointTestLayout = Layout.Id;
        _checkpointTestDraft = _session.DraftId;
        _checkpointTestDeadline = Time.realtimeSinceStartup + 30f;
        ReportFeedback("Preparing checkpoint test · waiting for the draft to synchronize…");
        Plugin.LogInfo(LastFeedback);
        StartPendingEditorMissionTest();
    }

    private async void PrepareLayoutCheckpointTest()
    {
        if (_checkpointTestPreparing || EditorMissionTestActive || AiPreviewBusy || _walking || !MissionContent || !EditorMode.Ready)
        {
            CheckpointTestMessage("Finish the current preview or walkthrough before testing checkpoints.");
            return;
        }
        var session = _session;
        if (session == null || session.Retired || session.Conflict != null)
        {
            CheckpointTestMessage("Connect the editor and resolve draft conflicts before testing checkpoints.");
            return;
        }
        var layout = Layout;
        if (layout == null)
        {
            CheckpointTestMessage("Select a layout before testing checkpoints.");
            return;
        }
        var errors = MapLayoutRules.Errors(layout, walkthrough: true);
        if (errors.Count > 0)
        {
            CheckpointTestMessage("Cannot test checkpoints: " + errors[0]);
            return;
        }
        _checkpointTestPreparing = true;
        var version = session.ContentVersion;
        var draftId = session.DraftId;
        session.Hold = true;
        try
        {
            var response = await EditorMissionTestClient.PrepareCheckpointsAsync(session.DraftId, layout.Id);
            if (_session != session || session.Retired || !this)
                return;
            if (
                session.ContentVersion != version
                || session.DraftId != draftId
                || Layout?.Id != layout.Id
                || session.Dirty
                || session.Conflict != null
            )
                throw new InvalidOperationException("The draft changed while preparing the test. Save it and try again.");
            _checkpointTestPreparing = false;
            session.Hold = false;
            StartEditorMissionTest(response);
        }
        catch (InvalidOperationException error)
        {
            CheckpointTestMessage("Checkpoint test: " + error.Message);
        }
        catch (Exception error)
        {
            CheckpointTestMessage("Checkpoint test: " + error.Message);
            Plugin.Error(error);
        }
        finally
        {
            if (!session.Previewing)
                session.Hold = false;
            _checkpointTestPreparing = false;
        }
    }

    internal void StartEditorMissionTest(EditorTestMissionResponse response)
    {
        if (
            response?.Descriptor?.Layout == null
            || response.Descriptor.Layout.Start == null
            || response.Descriptor.Layout.Exit == null
            || response.Descriptor.Layout.Checkpoints == null
            || response.Descriptor.Layout.Checkpoints.Count == 0
            || string.IsNullOrWhiteSpace(response.RunId)
        )
            throw new InvalidDataException("The editor mission test returned no route descriptor.");
        if (EditorMode.SelectedContentMode != WTT.Campaigns.Shared.Authoring.EditorContentMode.Mission || !EditorMode.Ready)
            throw new InvalidOperationException("The editor session is not ready for a mission test.");
        if (EditorMissionTestActive || AiPreviewBusy)
            throw new InvalidOperationException("Finish or reset the current editor test before starting another one.");

        _editorMissionTest = response;
        _editorMissionLayout = RaidEditorSession.Copy(response.Descriptor.Layout);
        _editorMissionGeneration++;
        _editorMissionRequested = true;
        _editorMissionUseEncounters = response.UseEncounters;
        _editorMissionCompleted = false;
        _editorMissionCheckpoint = 0;
        _editorMissionPending = response;
        ReportFeedback("Mission test will start when the editor workspace is ready…");
        StartPendingEditorMissionTest();
    }

    private async Task PrepareMissionPlaytest()
    {
        var session = _session!;
        var layout = Layout!;
        var version = session.ContentVersion;
        var draft = session.DraftId;
        var missions = session.Definition.Missions.AsValueEnumerable().Where(m => m.LayoutId == layout.Id).ToArray();
        var mission = missions.AsValueEnumerable().FirstOrDefault(m => m.Id == Ai.SelectedMissionId) ?? missions[0];
        _checkpointTestPreparing = true;
        session.Hold = true;
        ReportFeedback("Preparing mission playtest: " + mission.Name);
        try
        {
            var response = await EditorMissionTestClient.PrepareAsync(draft, layout.Id, mission.Id);
            if (_session != session || session.Retired || !this)
                return;
            if (
                session.ContentVersion != version
                || session.DraftId != draft
                || Layout?.Id != layout.Id
                || session.Dirty
                || session.Conflict != null
            )
                throw new InvalidOperationException("The draft changed while preparing the playtest. Save it and try again.");
            _checkpointTestPreparing = false;
            session.Hold = false;
            StartEditorMissionTest(response);
        }
        catch (Exception error)
        {
            Plugin.Error(error);
            ReportFeedback("Mission playtest unavailable: " + error.Message, ConsoleSeverity.Error);
        }
        finally
        {
            if (!session.Previewing)
                session.Hold = false;
            _checkpointTestPreparing = false;
        }
    }

    private void StartPendingEditorMissionTest()
    {
        if (_checkpointTestLayout.Length > 0)
        {
            if (
                _session == null
                || _session.Retired
                || _session.DraftId != _checkpointTestDraft
                || Layout?.Id != _checkpointTestLayout
                || !_open
            )
                _checkpointTestLayout = "";
            else if (_session.Conflict != null || Time.realtimeSinceStartup > _checkpointTestDeadline)
            {
                _checkpointTestLayout = "";
                CheckpointTestMessage("Checkpoint test could not start: " + _session.Status);
            }
            else if (!_session.Busy && !_session.Dirty)
            {
                _checkpointTestLayout = "";
                PrepareLayoutCheckpointTest();
            }
        }
        if (
            _editorMissionPending == null
            || _session == null
            || !_open
            || Layout == null
            || _walking
            || !MissionContent
            || !EditorMode.Ready
            || AiPreviewBusy
        )
            return;
        if (_session.Busy || _session.Dirty || _session.Conflict != null)
            return;
        _editorMissionPending = null;
        BeginAiPreview(true);
    }

    private async Task BeginEditorMissionRoute(MapLayout layout, CancellationToken token)
    {
        if (!_editorMissionRequested || _editorMissionTest == null)
            return;
        var route = _editorMissionLayout ?? RaidEditorSession.Copy(layout);
        if (route.Start == null || route.Exit == null || route.Checkpoints.Count == 0)
            throw new InvalidOperationException("The mission test layout needs a start, checkpoints and an exit.");
        var generation = _editorMissionGeneration;
        var runId = _editorMissionTest.RunId;
        _testEnvironment?.Dispose();
        _testEnvironment = new MissionEnvironment(_editorMissionTest.Descriptor!.Definition.Environment);
        _editorMissionLayout = RaidEditorSession.Copy(route);
        _editorMissionCheckpoint = 0;
        _editorMissionCompleted = false;
        _editorMissionProgressPending = false;
        _editorMissionLifetime?.Cancel();
        _editorMissionLifetime?.Dispose();
        _editorMissionLifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        var routeLifetime = _editorMissionLifetime;
        MissionLoot? loot = null;
        try
        {
            // Reconcile the frozen runtime layout after closing editor visuals.
            // This also removes inert loot previews before native loot is added.
            _mapScene ??= new();
            await _mapScene.ApplyAsync(route, true, routeLifetime.Token, runtime: true);
            // Use the native loot owner so placed items are interactable during
            // the rehearsal and are disposed with this run. The editor source
            // profile is never used as the storage target for collected items.
            loot = new MissionLoot();
            await loot.ApplyAsync(route, runId, routeLifetime.Token);
            routeLifetime.Token.ThrowIfCancellationRequested();
            if (!IsCurrentEditorMission(generation, runId, routeLifetime))
            {
                loot.Dispose();
                return;
            }
            foreach (var checkpoint in route.Checkpoints)
            {
                routeLifetime.Token.ThrowIfCancellationRequested();
                CreateEditorMissionVolume(checkpoint, "Checkpoint");
            }
            routeLifetime.Token.ThrowIfCancellationRequested();
            CreateEditorMissionVolume(route.Exit, "Exit");
            _editorMissionLoot = loot;
            loot = null;
        }
        finally
        {
            loot?.Dispose();
        }
        _aiPreviewStatus = "Mission test · reach checkpoint 1 of " + route.Checkpoints.Count;
        if (
            _aiRuntime != null
            && _player != null
            && (
                _editorMissionTest.Descriptor!.Definition.CheckpointRetries
                || MissionLogic.HasLogic(_editorMissionTest.Descriptor.Definition)
            )
        )
        {
            _editorDirector = new MissionDirector(route, _player, _aiRuntime);
            _editorDirector.BindInteractions(_mapScene!.MissionInteractions(route));
            _editorDirector.BindInteractions(_editorMissionLoot!.MissionInteractions(route));
        }
        if (_view?.Valid == true)
            _view.Text("EditorWalkStatus", _aiPreviewStatus + " · Esc to return to editing");
    }

    private void CreateEditorMissionVolume(MapVolume volume, string kind)
    {
        if (string.IsNullOrWhiteSpace(volume.Scene))
            throw new InvalidOperationException("The authored " + kind.ToLowerInvariant() + " has no scene.");
        var scene = SceneManager.GetSceneByName(volume.Scene);
        if (!scene.IsValid() || !scene.isLoaded)
            throw new InvalidOperationException("The authored " + kind.ToLowerInvariant() + " scene is unavailable: " + volume.Scene);
        var root = new GameObject("Editor mission " + kind + " " + volume.Name);
        root.layer = LayerMask.NameToLayer("Triggers");
        root.transform.SetPositionAndRotation(ZoneRuntime.Vector(volume.Position), Quaternion.Euler(ZoneRuntime.Vector(volume.Rotation)));
        if (volume.Shape == "Sphere")
        {
            var collider = root.AddComponent<SphereCollider>();
            collider.isTrigger = true;
            collider.radius = volume.Radius;
        }
        else if (volume.Shape == "Box")
        {
            var collider = root.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = ZoneRuntime.Vector(volume.Size);
        }
        else
        {
            Destroy(root);
            throw new InvalidOperationException("The authored " + kind.ToLowerInvariant() + " shape is unsupported.");
        }
        SceneManager.MoveGameObjectToScene(root, scene);
        root.AddComponent<EditorMissionRouteTrigger>().Initialize(this, kind, volume.Id);
        _editorMissionVolumes.Add(root);
    }

    private void EditorMissionRouteEntered(string kind, string id, Collider other)
    {
        if (
            !_editorMissionRequested
            || _editorMissionCompleted
            || _editorMissionProgressPending
            || _testRetryGuard?.Frozen == true
            || _editorMissionLayout == null
            || !Singleton<GameWorld>.Instantiated
        )
            return;
        var player = Singleton<GameWorld>.Instance.GetPlayerByCollider(other);
        if (!player || player != _player)
            return;
        if (_editorDirector?.HasPending == true || _editorSignals != null)
            return;
        if (kind == "Checkpoint")
        {
            if (
                _editorMissionCheckpoint >= _editorMissionLayout.Checkpoints.Count
                || _editorMissionLayout.Checkpoints[_editorMissionCheckpoint].Id != id
            )
                return;
            _ = ReportEditorMissionProgress(id, "Checkpoint");
        }
        else if (kind == "Exit" && _editorMissionCheckpoint == _editorMissionLayout.Checkpoints.Count)
        {
            _ = ReportEditorMissionProgress(id, "Exit");
        }
    }

    private async Task ReportEditorMissionProgress(string id, string kind)
    {
        if (_editorMissionTest == null || _editorMissionLayout == null || _editorMissionProgressPending)
            return;
        var generation = _editorMissionGeneration;
        var lifetime = _editorMissionLifetime;
        var runId = _editorMissionTest.RunId;
        _editorMissionProgressPending = true;
        try
        {
            var save = kind == "Checkpoint" && _testRetryGuard != null;
            if (
                !MissionLogic.CanAdvance(
                    _editorMissionTest.Descriptor!.Definition,
                    _editorMissionTest.Run!.Logic,
                    kind == "Checkpoint" ? id : "",
                    out var objectiveError
                )
            )
            {
                _aiPreviewStatus = objectiveError;
                return;
            }
            if (save)
            {
                _aiPreviewStatus = "Saving checkpoint…";
                _testRetryGuard!.Freeze();
                _editorDirector!.Pause();
                await _aiRuntime!.SettleAsync(lifetime!.Token);
                await MissionInventorySnapshot.SettleHands(_player!, lifetime.Token);
                await MissionWorldSnapshot.SettleAsync(lifetime.Token);
                await DrainTestObservations(lifetime.Token);
                if (_editorMissionTest.Run!.Logic.Failure.Length > 0)
                {
                    CheckTestFailure();
                    return;
                }
            }
            var response = await EditorMissionTestClient.ProgressAsync(
                _editorMissionTest,
                id,
                kind,
                lifetime?.Token ?? CancellationToken.None
            );
            if (!IsCurrentEditorMission(generation, runId, lifetime))
                return;
            MissionAcknowledgement.Require(_editorMissionTest.Run!, response.Run, response.Committed);
            _editorMissionTest = response;
            if (save)
                _testCheckpoint = new MissionRaidCheckpoint(id, _player!, _aiRuntime!);
            if (response.Run != null)
            {
                _editorDirector?.Accept(response.Run.Logic);
                _testHud?.Accept(response.Run);
            }
            if (save)
                ReleaseTestHold();
            var nextCheckpoint = response.Run?.NextCheckpointIndex ?? -1;
            if (nextCheckpoint < 0 || nextCheckpoint > _editorMissionLayout.Checkpoints.Count)
                throw new InvalidDataException("The mission test returned an invalid checkpoint index.");
            _editorMissionCheckpoint = nextCheckpoint;
            if (kind == "Checkpoint")
            {
                _aiPreviewStatus =
                    _editorMissionCheckpoint == _editorMissionLayout.Checkpoints.Count
                        ? "Mission test · reach the authored exit"
                        : "Mission test · reach checkpoint "
                            + (_editorMissionCheckpoint + 1)
                            + " of "
                            + _editorMissionLayout.Checkpoints.Count;
            }
            else
            {
                _editorMissionTest = await EditorMissionTestClient.EndAsync(_editorMissionTest, lifetime?.Token ?? CancellationToken.None);
                if (!IsCurrentEditorMission(generation, runId, lifetime))
                    return;
                _editorMissionCompleted = true;
                ReportFeedback("Mission complete. Returning to editing…");
                EndAiPreview();
                return;
            }
            if (_view?.Valid == true)
                _view.Text("EditorWalkStatus", _aiPreviewStatus);
        }
        catch (OperationCanceledException) when (lifetime?.IsCancellationRequested == true || generation != _editorMissionGeneration)
        {
            // Reset/return owns the cancellation and performs the rest of cleanup.
        }
        catch (Exception error)
        {
            ReportFeedback("Mission test progress failed: " + error.Message, ConsoleSeverity.Error);
            if (_testRetryGuard?.Frozen == true)
                BreakTestRestore(error);
            _aiPreviewStatus = LastFeedback;
            Plugin.Error(error);
            if (_view?.Valid == true)
                _view.Text("EditorWalkStatus", _aiPreviewStatus);
        }
        finally
        {
            if (generation == _editorMissionGeneration)
                _editorMissionProgressPending = false;
        }
    }

    private async Task RetryEditorMissionTest()
    {
        var previous = _editorMissionTest;
        if (previous == null || _editorMissionRetrying)
            return;
        _editorMissionRetrying = true;
        try
        {
            EndAiPreview();
            if (_aiReset != null)
                await _aiReset;
            var next = await EditorMissionTestClient.PrepareAsync(
                previous.DraftId,
                previous.LayoutId,
                previous.MissionId,
                previous.UseEncounters
            );
            _editorMissionTest = next;
            _editorMissionLayout = next.Descriptor?.Layout == null ? null : RaidEditorSession.Copy(next.Descriptor.Layout);
            _editorMissionGeneration++;
            _editorMissionRequested = true;
            _editorMissionUseEncounters = next.UseEncounters;
            _editorMissionCompleted = false;
            _editorMissionCheckpoint = 0;
            if (_session != null && _open)
                BeginAiPreview(true);
            else
                _editorMissionPending = next;
        }
        catch (Exception error)
        {
            _editorMissionTest = previous;
            _editorMissionRequested = true;
            _editorMissionCompleted = true;
            ReportFeedback("Mission test retry failed: " + error.Message, ConsoleSeverity.Error);
            Plugin.Error(error);
        }
        finally
        {
            _editorMissionRetrying = false;
        }
    }

    private void EndEditorMissionRoute(bool keepRequest = false)
    {
        _testEnvironment?.Dispose();
        _testEnvironment = null;
        EndTestRetry();
        _editorDirector?.Dispose();
        _editorDirector = null;
        _editorSignals = null;
        var run = _editorMissionTest;
        var resetServer = run != null && !keepRequest && !_editorMissionCompleted;
        _editorMissionGeneration++;
        _editorMissionLifetime?.Cancel();
        _editorMissionLifetime?.Dispose();
        _editorMissionLifetime = null;
        _editorMissionLoot?.Dispose();
        _editorMissionLoot = null;
        foreach (var volume in _editorMissionVolumes)
            if (volume)
                Destroy(volume);
        _editorMissionVolumes.Clear();
        _editorMissionProgressPending = false;
        _editorMissionCompleted = false;
        _editorMissionCheckpoint = 0;
        _editorMissionPending = null;
        if (!keepRequest)
        {
            _editorMissionRequested = false;
            _editorMissionTest = null;
            _editorMissionLayout = null;
        }
        if (resetServer)
            _ = ResetEditorMissionServerState(run!);
    }

    private bool IsCurrentEditorMission(long generation, string runId, CancellationTokenSource? lifetime = null) =>
        generation == _editorMissionGeneration
        && (lifetime == null || ReferenceEquals(lifetime, _editorMissionLifetime))
        && _editorMissionTest?.RunId == runId;

    private async Task ResetEditorMissionServerState(EditorTestMissionResponse run)
    {
        try
        {
            await EditorMissionTestClient.ResetAsync(run);
        }
        catch (Exception error)
        {
            // The session may already have unloaded its map. The server also
            // expires abandoned in-memory runs, so cleanup remains bounded.
            Plugin.LogInfo("Mission test cleanup deferred: " + error.Message);
        }
    }
}
