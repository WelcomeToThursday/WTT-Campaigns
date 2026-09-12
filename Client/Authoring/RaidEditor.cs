using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using UnityEngine;
using UnityEngine.EventSystems;
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
    internal bool InputBlocked
    {
        get { return _open; }
    }

    private ConfigEntry<bool> _enabled = null!;
    private ConfigEntry<KeyboardShortcut> _shortcut = null!;
    private RaidEditorSession? _session;
    private RaidEditorView? _view;
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
    private List<Transform> _scene = new();
    private CaptureTask? _task;
    private readonly List<(string Id, string Label)> _rows = new();
    private SpatialCapture? Selected
    {
        get
        {
            return _session
                ?.Definition?.Zones.AsValueEnumerable()
                .Cast<SpatialCapture>()
                .Concat(_session.Definition.Captures)
                .FirstOrDefault(z => z.Id == _selected);
        }
    }

    private void Awake()
    {
        Instance = this;
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
        try
        {
            var player = Plugin.InRaid && Plugin.Player?.HealthController?.IsAlive == true ? Plugin.Player : null;
            if (player != _player || !_enabled.Value || player && _enabled.Value && _session == null)
            {
                if (_session != null)
                {
                    Close();
                    _ = _session.Retire();
                    _session = null;
                }
                _player = player;
                _task = null;
                if (player && _enabled.Value && ZoneRuntime.Location.Length > 0)
                {
                    _session = new RaidEditorSession(ZoneRuntime.Location);
                    _session.NativeZoneIds = Resources
                        .FindObjectsOfTypeAll<EFT.Interactive.TriggerWithId>()
                        .AsValueEnumerable()
                        .Where(t => t && t.gameObject.scene.IsValid() && !t.GetComponentInParent<NativeZoneBridge>())
                        .Select(t => t.Id)
                        .Where(id => !string.IsNullOrEmpty(id))
                        .Distinct()
                        .ToList();
                    _session.Scenes = ValueEnumerable
                        .Range(0, UnityEngine.SceneManagement.SceneManager.sceneCount)
                        .Select(UnityEngine.SceneManagement.SceneManager.GetSceneAt)
                        .Where(s => s.isLoaded)
                        .Select(s => s.name)
                        .ToList();
                    _session.Changed += Refresh;
                    _lastContact = Time.realtimeSinceStartup;
                    _nextPoll = 0;
                }
            }
            if (!player || !_enabled.Value || _session == null)
            {
                return;
            }

            if (_shortcut.Value.IsDown())
            {
                if (_open)
                {
                    Close();
                }
                else if (!OtherModal && !Cursor.visible)
                {
                    Open();
                }
            }
            if (_open && (OtherModal || Time.realtimeSinceStartup - _lastContact > 20))
            {
                Close();
                _notice = "Editor closed because another screen or a connection interruption took priority.";
            }
            _session.Hold = _view?.Typing == true || _drag != null;
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

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (_drag != null)
                {
                    CancelDrag();
                }
                else if (_picking)
                {
                    _picking = false;
                }
                else
                {
                    Close();
                }
                return;
            }
            if (!_view!.Typing && !_session.Busy && _session.Conflict == null)
            {
                if (Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(KeyCode.Z))
                {
                    _session.Undo(false);
                }

                if (Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(KeyCode.Y))
                {
                    _session.Undo(true);
                }

                GeometryInput();
            }
            if (Time.realtimeSinceStartup >= _nextRefresh)
            {
                _nextRefresh = Time.realtimeSinceStartup + .25f;
                Refresh(false);
            }
        }
        catch (Exception e)
        {
            _notice = e.Message;
            Plugin.Error(e);
            Close();
        }
    }

    private async void Poll()
    {
        var session = _session!;
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
    }

    private void Open()
    {
        if (_open || OtherModal || !_player)
        {
            return;
        }

        _view ??= BuildView();
        _camera = Camera.main;
        if (!_camera)
        {
            throw new InvalidOperationException("The raid camera is not ready.");
        }

        _savedPosition = _flyPosition = _camera!.transform.position;
        _savedRotation = _flyRotation = _camera.transform.rotation;
        _savedCursor = Cursor.visible;
        _savedLock = Cursor.lockState;
        _open = true;
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
        _view.Root.SetActive(true);
        IndexScene();
        Refresh();
    }

    private void CameraPose(Camera camera)
    {
        if (_open && camera == _camera)
        {
            camera.transform.SetPositionAndRotation(_flyPosition, _flyRotation);
        }
    }

    private void LateUpdate()
    {
        if (!_open || !_camera)
        {
            return;
        }

        try
        {
            if (_view?.Typing != true && _drag == null && _session?.Conflict == null)
            {
                var look = Input.GetMouseButton(1);
                Cursor.visible = !look;
                Cursor.lockState = look ? CursorLockMode.Locked : CursorLockMode.None;
                if (look)
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
                    _flyPosition += direction * Time.unscaledDeltaTime * (Input.GetKey(KeyCode.LeftShift) ? 24 : 6);
                }
            }
            _camera!.transform.SetPositionAndRotation(_flyPosition, _flyRotation);
        }
        catch (Exception e)
        {
            Plugin.Error(e);
            Close();
        }
    }

    private void Close()
    {
        if (!_open)
        {
            return;
        }

        try
        {
            CancelDrag();
            _picking = false;
            _session?.Persist();
        }
        finally
        {
            _open = false;
            Camera.onPreCull -= CameraPose;
            if (_camera)
            {
                _camera!.transform.SetPositionAndRotation(_savedPosition, _savedRotation);
            }

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
            _view?.Root.SetActive(false);
            ClearLines();
            _camera = null;
        }
    }

    private void OnDestroy()
    {
        Close();
        if (_session != null)
        {
            _ = _session.Retire();
        }

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

        _task = task;
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
