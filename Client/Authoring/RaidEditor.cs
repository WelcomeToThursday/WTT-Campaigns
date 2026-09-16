using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using UnityEngine;
using UnityEngine.EventSystems;
using WTT.Campaigns.Client.Authoring.Rendering;
using WTT.Campaigns.Client.Authoring.Views;
using WTT.Campaigns.Client.Spatial;
using WTT.Campaigns.Client.Story;
using WTT.Campaigns.Shared.Authoring;
using WTT.Campaigns.Shared.Spatial;
using ZLinq;

namespace WTT.Campaigns.Client.Authoring;

[DefaultExecutionOrder(32000)]
public sealed partial class RaidEditor : MonoBehaviour
{
    internal static RaidEditor? Instance;
    internal static bool KeepNotificationConnection => EditorMode.Active && !EditorMode.Returning || Instance && Instance!._enabled.Value;
    internal bool InputBlocked
    {
        get { return _open; }
    }

    private ConfigEntry<float> _cameraSpeed = null!;
    private ConfigEntry<bool> _enabled = null!;
    private ConfigEntry<KeyboardShortcut> _shortcut = null!;
    private RaidEditorSession? _session;
    private RaidEditorView? _view;
    private readonly EditorOpenState _openState = new();
    private Player? _player;
    private float _nextPoll,
        _lastContact,
        _nextRefresh;
    private bool _open,
        _snap = true;
    private Camera? _camera;
    private Vector3 _savedPosition,
        _flyPosition;
    private Quaternion _savedRotation,
        _flyRotation;
    private bool _savedCursor;
    private bool _looking;

    // Latch before native input locks/centers the pointer, so crossing a panel
    // while flying cannot release capture. Release on RMB up or blocked input.
    internal bool CameraLooking =>
        _looking =
            _open
            && Application.isFocused
            && Input.GetMouseButton(1)
            && _view?.Typing != true
            && _drag == null
            && _session?.Conflict == null
            && _view?.Windows.HasMenu != true
            && (_looking || EventSystem.current?.IsPointerOverGameObject() != true && _view?.PointerOver != true);
    private CursorLockMode _savedLock;
    private GameObject? _events;
    private readonly List<EventSystem> _disabledEvents = new();
    private readonly List<(Renderer Renderer, bool Enabled)> _renderers = new();
    private string _selected = "",
        _mode = "Zones",
        _tool = "Move",
        _notice = "";
    private int _page;
    private Transform? _picked;
    private CaptureTask? _task;

    private SpatialCapture? Selected
    {
        get
        {
            if (_mode == "AI")
                return AiSelectedPoint();
            if (MapWorkspace || SceneWorkspace)
                return MapPoint ?? (SceneWorkspace && _sceneTab != "Catalog" ? _sceneSelectionPose : null);
            return _session
                ?.Definition?.Zones.AsValueEnumerable()
                .Cast<SpatialCapture>()
                .Concat(_session.Definition.Captures)
                .FirstOrDefault(z => z.Id == _selected);
        }
    }

    private bool AuthoringEnabled => EditorMode.Active ? EditorMode.Ready : _enabled.Value;

    private void Awake()
    {
        Instance = this;
        _cameraSpeed = Plugin.Instance.Config.Bind(
            "Raid authoring",
            "Camera speed",
            6f,
            new ConfigDescription(
                "Editor camera movement in metres per second. Shift boosts 4x; Ctrl slows to one quarter.",
                new AcceptableValueRange<float>(.25f, 96f)
            )
        );
        _enabled = Plugin.Instance.Config.Bind(
            "Raid authoring",
            "Enable authoring",
            false,
            "Advertise this raid to the administrator's campaign editor. Draft previews never execute gameplay actions."
        );
        _shortcut = Plugin.Instance.Config.Bind(
            "Raid authoring",
            "Editor shortcut",
            new KeyboardShortcut(KeyCode.F8, KeyCode.LeftControl),
            "Open the in-raid authoring tools. The raid continues while using the free camera."
        );
    }

    private bool OtherModal
    {
        get
        {
            return Plugin.Busy
                || StoryPresentationDispatcher.Active
                || (StoryVisitRuntime.Instance && StoryVisitRuntime.Instance.InputBlocked)
                || (StoryCinematicRuntime.Instance && StoryCinematicRuntime.Instance.InputBlocked)
                || (UI.SeasonUi.Instance && UI.SeasonUi.Instance.InputBlocked)
                || OtherEditor();
        }
    }

    private static bool OtherEditor()
    {
        using var diagnostic = EditorDiagnostics.Measure(EditorDiagnostics.Area.OtherEditor);
        foreach (var plugin in BepInEx.Bootstrap.Chainloader.PluginInfos.Values)
        {
            foreach (var component in plugin.Instance.GetComponents<MonoBehaviour>())
            {
                if (!component || component.GetType().FullName != "MapLootEditor.Client.InRaidEditor")
                {
                    continue;
                }

                if (
                    component
                        .GetType()
                        .GetProperty(
                            "IsOpen",
                            System.Reflection.BindingFlags.Instance
                                | System.Reflection.BindingFlags.Public
                                | System.Reflection.BindingFlags.NonPublic
                        )
                        ?.GetValue(component)
                    is true
                )
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void Update()
    {
        using var diagnostic = EditorDiagnostics.Measure(EditorDiagnostics.Area.FrameUpdate);
        try
        {
            var player =
                Plugin.InRaid && (!EditorMode.Active || EditorMode.MapReady) && Plugin.Player?.HealthController?.IsAlive == true
                    ? Plugin.Player
                    : null;
            if (player != _player || !AuthoringEnabled || player && AuthoringEnabled && _session == null)
            {
                if (_session != null)
                {
                    EndWalkthrough();
                    Close();
                    _ = _session.Retire();
                    _session = null;
                }
                else if (EditorMissionTestActive && (_editorMissionPending == null || !player || !AuthoringEnabled))
                {
                    EndEditorMissionRoute();
                }
                ClearSceneIndex();
                _player = player;
                _openState.Reset();
                _task = null;
                if (player && AuthoringEnabled && ZoneRuntime.Location.Length > 0)
                {
                    _toolStates.Clear();
                    _session = new RaidEditorSession(ZoneRuntime.Location);
                    if (EditorMode.Ready)
                    {
                        _layoutId = EditorMode.SelectedLayout;
                        _mode = "Layouts";
                        _selected = _layoutId;
                    }
                    else if (MapWorkspace)
                    {
                        _mode = "Zones";
                        _selected = "";
                        _layoutId = "";
                    }
                    _session.NativeZoneIds = Resources
                        .FindObjectsOfTypeAll<EFT.Interactive.TriggerWithId>()
                        .AsValueEnumerable()
                        .Where(t => t && t.gameObject.scene.IsValid() && !t.GetComponentInParent<NativeZoneBridge>())
                        .Select(t => t.Id)
                        .Where(id => !string.IsNullOrEmpty(id))
                        .Distinct()
                        .ToList();
                    RefreshLoadedScenes();
                    _session.Changed += Refresh;
                    _lastContact = Time.realtimeSinceStartup;
                    _nextPoll = 0;
                }
            }
            if (!player || !AuthoringEnabled || _session == null)
            {
                return;
            }

            StartPendingEditorMissionTest();

            if (UpdateAiPreview())
                return;

            if (_walking && _shortcut.Value.IsDown())
            {
                EndWalkthrough(returnToEditor: true);
                return;
            }
            if (_shortcut.Value.IsDown() && _view?.Typing != true)
            {
                if (_open)
                {
                    Close();
                }
                else if (!OtherModal && !Cursor.visible)
                {
                    Open(true);
                }
            }
            if (_open && OtherModal)
            {
                EndWalkthrough();
                Close();
                _notice = "Editor closed because another screen took priority.";
            }
            // Keep draft controls and recovery accessible while the native socket
            // reconnects. Only the applied physical walkthrough needs to stop.
            if (_walking && Time.realtimeSinceStartup - _lastContact > 20)
            {
                EndWalkthrough(returnToEditor: true);
                _notice = "Walkthrough restored while the editor connection recovers.";
                return;
            }
            // Escape belongs to the walkthrough for this entire frame. Reopening
            // here would let the same key close the editor and consume its bookmark.
            if (UpdateWalkthrough())
                return;
            if (EditorMode.Ready && !_open && !_walking && !AiPreviewBusy && !OtherModal && _session.Definition != null)
                Open();
            _session.Hold = AiPreviewBusy || _walking || _view?.Typing == true || _drag != null || _placementLifetime != null;
            if (!_session.Busy && Time.realtimeSinceStartup >= _nextPoll)
            {
                _nextPoll = Time.realtimeSinceStartup + 1;
                Poll();
            }
            if (_task != null && _session.Tasks.AsValueEnumerable().Any(t => t.Id == _task.Id && t.Status is "Completed" or "Cancelled"))
            {
                _task = null;
            }

            if (
                _task == null
                && !_walking
                && !AiPreviewBusy
                && !_session.Hold
                && !_session.Dirty
                && _session.Conflict == null
                && !_session.Busy
                && !OtherModal
                && (!_open ? !Cursor.visible : !_view!.Typing && _drag == null)
            )
            {
                var task = _session.Tasks.AsValueEnumerable().FirstOrDefault(t => t.Status == "Pending");
                if (task != null)
                {
                    BeginTask(task);
                }
            }
            if (!_open)
            {
                return;
            }

            AdvanceSceneIndex();
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (_view!.Windows.DismissMenus())
                    return;
                if (_view.DismissDropdowns())
                    return;
                if (_view.Typing)
                {
                    _view.ReleaseFocus();
                    EventSystem.current?.SetSelectedGameObject(null);
                    return;
                }
                if (_placementLifetime != null)
                {
                    CancelPlacement();
                }
                else if (_drag != null)
                {
                    CancelDrag();
                }
                else if (_picking)
                {
                    _picking = false;
                    _sceneRebindId = "";
                }
                else
                {
                    Close();
                }
                return;
            }
            if (!AiPreviewBusy && !_view!.Typing && !_session.Retired && _session.Conflict == null && !_view.Windows.HasMenu)
            {
                if (Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(KeyCode.Z))
                {
                    CancelDrag();
                    _session.Undo(false);
                }

                if (Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(KeyCode.Y))
                {
                    CancelDrag();
                    _session.Undo(true);
                }

                if (Input.GetKeyDown(KeyCode.F))
                    FrameSceneSelection();
                if (
                    !Input.GetMouseButton(1)
                    && _drag == null
                    && _placementLifetime == null
                    && !Input.GetKey(KeyCode.LeftControl)
                    && !Input.GetKey(KeyCode.RightControl)
                    && !Input.GetKey(KeyCode.LeftAlt)
                    && !Input.GetKey(KeyCode.RightAlt)
                    && !Input.GetKey(KeyCode.LeftShift)
                    && !Input.GetKey(KeyCode.RightShift)
                )
                {
                    var tool =
                        Input.GetKeyDown(KeyCode.W) ? "Move"
                        : Input.GetKeyDown(KeyCode.E) ? "Rotate"
                        : Input.GetKeyDown(KeyCode.R) ? "Scale"
                        : "";
                    if (tool.Length > 0 && (SceneWorkspace ? CanTransformScene(tool) : Selected != null || MapPoint != null))
                    {
                        if (SceneWorkspace)
                            SceneTransform(tool);
                        else
                            _tool = tool;
                        Refresh();
                    }
                }
                if (!PlacementInput())
                    GeometryInput();
            }
            if (Time.realtimeSinceStartup >= _nextRefresh)
            {
                _nextRefresh = Time.realtimeSinceStartup + .25f;
                RefreshPassive();
            }
        }
        catch (Exception e)
        {
            _openState.Fail();
            EndWalkthrough();
            _notice = e.Message;
            Plugin.Error(e);
            Plugin.LogInfo("Editor automatic opening paused after an error. Press the editor shortcut to retry, or reopen the map.");
            Close();
        }
    }

    private async void Poll()
    {
        var session = _session!;
        RefreshLoadedScenes();
        await session.Tick();
        if (_session != session || session.Retired)
        {
            return;
        }

        if (session.Contacted)
        {
            _lastContact = Time.realtimeSinceStartup;
        }
        else
        {
            _nextPoll = Time.realtimeSinceStartup + 5;
        }

        if (session.Grant.Length == 0 && _task != null)
        {
            _task = null;
            Close();
        }
        if (_walkRequested && !session.Dirty && !session.Busy)
        {
            _walkRequested = false;
            if (_open && session.Grant.Length > 0 && session.Conflict == null)
                BeginWalkthrough();
        }
        if (_aiRequested.HasValue && !session.Dirty && !session.Busy && session.Conflict == null)
        {
            var playtest = _aiRequested.Value;
            _aiRequested = null;
            BeginAiPreview(playtest);
        }
    }

    private void Open(bool requested = false)
    {
        if (_open || OtherModal || !_player)
        {
            return;
        }

        if (!_openState.TryBegin(requested))
            return;

        if (_view?.Valid != true)
        {
            _view?.Dispose();
            Plugin.LogInfo("Editor loading: constructing workspace");
            _view = BuildView();
            Plugin.LogInfo("Editor loading: workspace constructed");
        }
        _camera = Camera.main;
        if (!_camera)
        {
            throw new InvalidOperationException("The raid camera is not ready.");
        }

        _savedPosition = _camera!.transform.position;
        _savedRotation = _camera.transform.rotation;
        // Camera.main can still carry the previous editor/map pose. The current
        // player's Cam anchor belongs to this raid and is independent of free flight.
        _flyPosition = _player!.CameraPosition.position;
        _flyRotation = Quaternion.LookRotation(_player.LookDirection);
        _looking = false;
        RestoreCameraBookmark();
        RestoreWalkCamera();
        _camera.transform.SetPositionAndRotation(_flyPosition, _flyRotation);
        _savedCursor = Cursor.visible;
        _savedLock = Cursor.lockState;
        _open = true;
        _environment = new EditorEnvironment(_camera, target => _mapScene?.IsHidden(target) == true);
        _environmentError = "";
        _weatherError = "";
        foreach (var renderer in _camera.GetComponentsInChildren<Renderer>(true))
        {
            _renderers.Add((renderer, renderer.enabled));
            renderer.enabled = false;
        }
        foreach (var system in FindObjectsOfType<EventSystem>().AsValueEnumerable().Where(e => e.enabled))
        {
            _disabledEvents.Add(system);
            system.enabled = false;
        }
        _events = new GameObject("Campaign authoring input", typeof(EventSystem), typeof(StandaloneInputModule));
        Camera.onPreCull += CameraPose;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        _view.SetVisible(true);
        Plugin.LogInfo("Editor loading: indexing scene");
        IndexScene();
        Plugin.LogInfo("Editor loading: presenting workspace");
        Refresh();
        RefreshEnvironment();
        RefreshWeather();
    }

    private void CameraPose(Camera camera)
    {
        if (_open && camera == _camera)
        {
            camera.transform.SetPositionAndRotation(_flyPosition, _flyRotation);
            _environment?.Pose(_flyPosition, _flyRotation);
        }
    }

    private void LateUpdate()
    {
        using var diagnostic = EditorDiagnostics.Measure(EditorDiagnostics.Area.LateUpdate);
        if (!_open || !_camera)
        {
            return;
        }

        try
        {
            _looking = CameraLooking;
            Cursor.lockState = _looking ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !_looking;
            if (_looking)
            {
                var angles = _flyRotation.eulerAngles;
                angles.x -= Input.GetAxis("Mouse Y") * 2;
                angles.y += Input.GetAxis("Mouse X") * 2;
                _flyRotation = Quaternion.Euler(angles);
                var direction = new Vector3(
                    (Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0),
                    0,
                    (Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0)
                );
                direction = _flyRotation * direction;
                direction.y += (Input.GetKey(KeyCode.E) ? 1 : 0) - (Input.GetKey(KeyCode.Q) ? 1 : 0);
                _flyPosition +=
                    Vector3.ClampMagnitude(direction, 1)
                    * Time.unscaledDeltaTime
                    * CameraSpeed
                    * (
                        Input.GetKey(KeyCode.LeftShift) ? 4
                        : Input.GetKey(KeyCode.LeftControl) ? .25f
                        : 1
                    );
            }
            _camera!.transform.SetPositionAndRotation(_flyPosition, _flyRotation);
            _environment?.Pose(_flyPosition, _flyRotation);
            // Refresh native rendering once at the final pose, after center-anchor corrections.
            _mapScene?.FlushVisuals();
            // Handles follow camera distance and pointer hover every frame, after scene poses reconcile.
            DrawGeometry();
        }
        catch (Exception e)
        {
            _openState.Fail();
            Plugin.Error(e);
            Close();
        }
    }

    private void Close()
    {
        _walkRequested = false;
        if (!_open)
        {
            return;
        }

        try
        {
            SaveCameraBookmark();
            CancelDrag();
            _picking = false;
            _sceneRebindId = "";
            CancelPlacement();
            _view?.Windows.DismissMenus();
            _session?.Persist();
        }
        finally
        {
            _open = false;
            _looking = false;
            Camera.onPreCull -= CameraPose;
            if (_camera)
            {
                _camera!.transform.SetPositionAndRotation(_savedPosition, _savedRotation);
            }
            _environment?.Dispose();
            _environment = null;

            foreach (var entry in _renderers)
            {
                if (entry.Renderer)
                {
                    entry.Renderer.enabled = entry.Enabled;
                }
            }

            _renderers.Clear();
            if (_events)
            {
                Destroy(_events);
            }

            _events = null;
            foreach (var system in _disabledEvents)
            {
                if (system)
                {
                    system.enabled = true;
                }
            }

            _disabledEvents.Clear();
            Cursor.visible = _savedCursor;
            Cursor.lockState = _savedLock;
            if (_view?.Valid == true)
                _view.SetVisible(false);
            ClearLines();
            ClearAiRoutes();
            _camera = null;
        }
    }

    private void OnDestroy()
    {
        EndWalkthrough();
        EndEditorMissionRoute();
        Close();
        if (_session != null)
        {
            _ = _session.Retire();
        }

        ClearSceneIndex();
        _view?.Dispose();
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void BeginTask(CaptureTask task)
    {
        if (!_open)
        {
            Open();
        }

        if (!_open)
        {
            return;
        }

        if (task.Tool == "MapLayout")
        {
            _layoutId = _selected = task.RecordId;
            _mode = "Layouts";
            _task = null;
            _session!.TaskStatus(task, "Completed", task.RecordId);
            Refresh();
            return;
        }
        _task = task;
        if (
            task.Tool == "Zone"
            && EditorMode.Ready
            && _session?.Definition?.Zones.AsValueEnumerable().FirstOrDefault(z => z.Id == task.RecordId) is { } taskZone
            && !string.IsNullOrEmpty(taskZone.LayoutId)
            && _session.Definition.MapLayouts.AsValueEnumerable().Any(l => l.Id == taskZone.LayoutId)
        )
            _layoutId = taskZone.LayoutId;
        _selected = task.RecordId;
        _mode = task.Tool == "Zone" ? "Zones" : "Captures";
        _notice =
            task.Tool == "Zone"
                ? "Place or edit a zone, then complete the capture."
                : "Pick a scene object or capture a transform, then complete.";
        _session!.TaskStatus(task, "Opened");
        Refresh();
    }
}
