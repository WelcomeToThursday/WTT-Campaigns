using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WTT.Campaigns.UI.Controls;

// Presentation state survives category changes and walkthroughs without rebuilding input fields.

public sealed partial class RaidEditorWindows : MonoBehaviour
{
    private sealed class Panel
    {
        public RectTransform Rect = null!;

        public EditorWindowDrag Drag = null!;

        public EditorWindowResize Resize = null!;
        public Vector2 Minimum;
        public bool Shown;

        public bool Floating,
            Visible;
    }

    private readonly Dictionary<string, Panel> _panels = new Dictionary<string, Panel>();

    private readonly Dictionary<string, Transform> _controls = new Dictionary<string, Transform>();

    private RectTransform _root = null!;

    private Transform _dock = null!;

    private Vector2 _size;

    private string _selection = "",
        _category = "";

    private bool _walkthrough;

    private readonly EditorTarkovTheme _theme = new EditorTarkovTheme();

    private readonly string[] _menus = { "WindowsMenu", "ContextMenu" };

    public void Initialize()
    {
        _root = (RectTransform)transform;

        foreach (var child in GetComponentsInChildren<Transform>(true))
            if (!_controls.ContainsKey(child.name))
                _controls.Add(child.name, child);

        _dock = _controls["ToolWindows"];
        _theme.Apply(gameObject);
        RegisterWindow("Library", "LibraryTitleBar", "LibraryCollapse", new Vector2(400, 390));
        RegisterWindow("Inspector", "InspectorTitleBar", "InspectorCollapse", new Vector2(360, 320));
        RegisterWindow("EnvironmentMenu", "EnvironmentMenuTitleBar", "EnvironmentClose", new Vector2(360, 320));
        RegisterWindow("Controls", "ControlsTitleBar", "HelpClose", new Vector2(440, 220));
        Bind("LibraryToggle", () => ToggleWindow("Library"));
        Bind("InspectorToggle", () => ToggleWindow("Inspector"));
        Bind("WindowsToggle", () => ToggleMenu("WindowsMenu"));
        Bind("ContextToggle", () => ToggleMenu("ContextMenu"));
        Bind("EnvironmentToggle", () => ToggleWindow("EnvironmentMenu"));
        Bind("EnvironmentWindowToggle", () => ToggleWindow("EnvironmentMenu"));
        Bind("HelpToggle", () => ToggleWindow("Controls"));
        Bind("HelpWindowToggle", () => ToggleWindow("Controls"));
        Bind("ResetLayout", ResetLayout);
        Bind(
            "RecordDetailsToggle",
            () =>
            {
                _recordDetails = !_recordDetails;
                UpdateDetails();
            }
        );
        Bind(
            "MapDetailsToggle",
            () =>
            {
                _mapDetails = !_mapDetails;
                UpdateDetails();
            }
        );
        CachePropertyGeometry();

        foreach (var button in GetComponentsInChildren<Button>(true))
        {
            var control = button;
            var tip = button.gameObject.AddComponent<EditorControlTooltip>();
            tip.Enter = () => ShowTooltip(control.name, control.GetComponentInChildren<Text>().text);
            tip.Exit = () => Visible("EditorTooltip", false);
        }
        foreach (var name in new[] { "Connection", "Status" })
        {
            var label = _controls[name].GetComponent<Text>();
            label.raycastTarget = true;
            var tip = label.gameObject.AddComponent<EditorControlTooltip>();
            tip.Enter = () => ShowTooltip(name, label.text);
            tip.Exit = () => Visible("EditorTooltip", false);
        }
        SetTooltip("Maps", "Mission layouts, barriers and scenery edits");
        SetTooltip("Routes", "Player start, ordered checkpoints and exit for the selected layout");
        SetTooltip("CameraSlower", "Halve camera speed · Shift: 4x boost · Ctrl: precision movement");
        SetTooltip("CameraFaster", "Double camera speed · Enter 0.25–96 metres per second in the field");
        SetTooltip("MapCheckpoint", "Place on the floor beneath the camera; insert after the selected checkpoint");
        SetTooltip("MapStart", "Set the player start on the floor beneath the camera, facing the camera heading");
        SetTooltip("MapExit", "Set the route exit on the floor beneath the camera");
        SetTooltip("MapAtPlayer", "Move the selected marker to the floor beneath the camera");
        SetTooltip("Zones", "Quest and story volumes for this location");
        SetTooltip("Bindings", "Raid events: trigger, interaction and scene targets");
        SetTooltip("Captures", "Named camera transforms and scene references");
        SetTooltip("Scene", "Search or pick existing scenery for a binding or map edit");
        SetTooltip("Undo", "Ctrl+Z · Undo the last draft edit");
        SetTooltip("Redo", "Ctrl+Y · Redo the last undone draft edit");
        SetTooltip("Move", "Move the selected record with the axis handles");
        SetTooltip("Rotate", "Drag a colored rotation ring, or enter rotation in Properties");
        SetTooltip("Scale", "Resize a selected static prop or volume. Native loot and containers retain their original size.");
        SetTooltip("Snap", "Snap to 5 cm / 5 degrees. Hold Alt while dragging to bypass.");
        SetTooltip("SceneFrame", "Frame selected object (F). Available outside typing, dragging and placement.");
        SetTooltip("SceneAnchor", "Center rotates and resizes around the visible object. Pivot uses its original origin.");
        RefreshBounds();
        ResetLayout();
    }

    private readonly Dictionary<string, string> _tips = new Dictionary<string, string>();

    public void SetTooltip(string name, string text) => _tips[name] = text;

    private void ShowTooltip(string name, string fallback)
    {
        if (_controls["ConflictShield"].gameObject.activeSelf || _walkthrough)
            return;
        var text = _tips.TryGetValue(name, out var value) ? value : fallback;
        if (text != fallback && !string.IsNullOrWhiteSpace(fallback))
            text = fallback.TrimStart('+', ' ') + "\n" + text;
        if (string.IsNullOrWhiteSpace(text))
            return;
        var label = _controls["EditorTooltipText"].GetComponent<Text>();
        label.text = text;
        var rect = (RectTransform)_controls["EditorTooltip"];
        // Measure in canvas units so short captions stay compact at every UI scale.
        var width = Mathf.Clamp(Mathf.Ceil(label.preferredWidth) + 24, 40, Mathf.Min(360, _root.rect.width - 16));
        var textRect = label.rectTransform;
        textRect.anchorMin = textRect.anchorMax = new Vector2(.5f, .5f);
        textRect.pivot = new Vector2(.5f, .5f);
        textRect.anchoredPosition = Vector2.zero;
        textRect.sizeDelta = new Vector2(width - 24, 0);
        var height = Mathf.Min(Mathf.Ceil(label.preferredHeight) + 16, _root.rect.height - 16);
        textRect.sizeDelta = new Vector2(width - 24, height - 16);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
        rect.pivot = new Vector2(0, 1);
        var canvas = GetComponent<Canvas>();
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _root,
            Input.mousePosition,
            canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera,
            out var point
        );
        var x = point.x + 14;
        var y = point.y - 18;
        if (x + width > _root.rect.xMax - 8)
            x = point.x - width - 14;
        if (y - height < _root.rect.yMin + 8)
            y = point.y + height + 18;
        rect.anchoredPosition = new Vector2(
            Mathf.Clamp(x, _root.rect.xMin + 8, _root.rect.xMax - width - 8),
            Mathf.Clamp(y, _root.rect.yMin + height + 8, _root.rect.yMax - 8)
        );
        rect.gameObject.SetActive(true);
        rect.SetAsLastSibling();
    }

    private void Bind(string name, Action action) => _controls[name].GetComponent<Button>().onClick.AddListener(() => action());

    public bool HasMenu
    {
        get
        {
            foreach (var name in _menus)
                if (_controls[name].gameObject.activeSelf)
                    return true;
            return false;
        }
    }

    public bool DismissMenus()
    {
        Visible("EditorTooltip", false);
        var dismissed = HasMenu;

        foreach (var name in _menus)
            _controls[name].gameObject.SetActive(false);

        return dismissed;
    }

    private void ToggleMenu(string name)
    {
        var show = !_controls[name].gameObject.activeSelf;
        DismissMenus();

        _controls[name].gameObject.SetActive(show);
        _controls["MenuLayer"].SetAsLastSibling();
        KeepModalOnTop();
    }

    public void ShowPanel(string id, bool visible)
    {
        _panels[id].Visible = visible;

        ApplyVisibility();
        if (visible)
            FocusWindow(_panels[id].Rect);
        LayoutChanged?.Invoke();
    }

    private bool _capture;

    private void Visible(string name, bool value)
    {
        var go = _controls[name].gameObject;
        if (go.activeSelf != value)
            go.SetActive(value);
    }

    public void Present(
        string mode,
        string kind,
        bool hasSelection,
        bool capture,
        bool picked,
        bool mapReady,
        bool bindZone,
        bool sceneWorkspace = false
    )
    {
        var routes = mode == "Routes" && mapReady;
        var maps = (mode == "Maps" || routes) && mapReady;
        Visible("RouteGuideGroup", routes);
        Visible("RouteFrameGroup", routes && hasSelection && kind != "Layout");
        Visible("MapWalkGroup", routes);

        var zone = mode == "Zones" && hasSelection;

        var point = hasSelection && kind != "Scene" && (zone || mode == "Captures");

        if (!sceneWorkspace)
        {
            Visible("MapInspector", maps);
            Visible("RecordInspector", !maps);
        }

        Visible("EditorMapToolbar", mapReady);
        Visible("EditorUnload", mapReady);

        Visible("NameGroup", hasSelection && mode != "Scene" && kind != "Scene");

        _identityAvailable = hasSelection;

        Visible("EventKindGroup", mode == "Bindings" && hasSelection && kind != "Scene");

        Visible("PositionGroup", point);
        Visible("RotationGroup", point);

        Visible("SizeGroup", zone && kind == "Box");
        Visible("RadiusGroup", zone && kind == "Sphere");

        Visible("PlacementGroup", point);
        Visible("ZoneUsesGroup", zone);

        Visible("SceneActionsGroup", picked || bindZone);

        Visible("RecordActionsGroup", hasSelection && mode != "Scene" && kind != "Scene");

        _recordAvailable = hasSelection || picked;

        if (!sceneWorkspace)
        {
            Visible("MapPositionGroup", hasSelection && kind != "Layout" && kind != "Door");

            Visible("MapRotationGroup", hasSelection && kind != "Layout" && kind != "Door");

            Visible("MapSizeGroup", kind == "Volume" || kind == "Checkpoint" || kind == "Copy");

            Visible("MapShapeGroup", kind == "Volume" || kind == "Checkpoint");

            Visible("MapPlacementGroup", hasSelection && kind != "Layout");

            Visible("MapRebind", kind == "Copy" || kind == "Move" || kind == "Hide" || kind == "Door");

            Visible("MapAtPlayer", kind != "Door");

            Visible("MapOrderGroup", kind == "Checkpoint");

            Visible("MapRecordActions", hasSelection && (!routes || kind != "Layout"));
        }

        Visible("AddBox", mode == "Zones" || mode == "Bindings");
        Visible("AddSphere", mode == "Zones" || mode == "Bindings");

        Visible("Capture", mode == "Captures");
        Visible("Pick", mode == "Scene" || mode == "Captures" || mode == "Bindings" || mode == "Maps");

        foreach (
            var name in new[]
            {
                "MapNew",
                "MapStart",
                "MapCheckpoint",
                "MapExit",
                "MapBarrier",
                "MapMoveObject",
                "MapCopyObject",
                "MapHideObject",
                "MapDoor",
            }
        )
            Visible(name, name == "MapStart" || name == "MapCheckpoint" || name == "MapExit" ? routes : maps && !routes);

        Visible("CaptureTask", capture);

        if (_capture != capture)
        {
            _capture = capture;
            FitPanels();
        }
        FitContents();
        UpdateDetails();
    }

    public void Select(string category, string selection)
    {
        if (_category == category && _selection == selection)
            return;

        var categoryChanged = _category != category;

        _category = category;
        _selection = selection;

        if (selection.Length > 0)
            ShowPanel("Inspector", true);
        else
        {
            ShowPanel("Inspector", false);
            if (categoryChanged)
                ShowPanel("Library", true);
        }

        _controls["PropertyScroll"].GetComponent<ScrollRect>().verticalNormalizedPosition = 1;
    }

    public void BrowseCategory() => ShowPanel("Library", true);

    public void PresentScene(
        bool enabled,
        string tab,
        string kind,
        bool selected,
        bool hasPoint,
        bool canEdit,
        bool picked,
        bool hasSavedPoint = true
    )
    {
        var catalog = tab == "Catalog";
        var removed = kind == "Hide";
        Visible("SceneTabs", enabled);
        Visible("SceneFilters", enabled && catalog);
        Visible("SceneInspector", enabled);
        if (enabled)
            Visible("MapWalkGroup", false);
        FitContents();
        if (!enabled)
            return;
        Visible("RecordInspector", false);
        Visible("MapInspector", !catalog && hasPoint);
        Visible("MapRecordActions", false);
        Visible("MapPositionGroup", !removed);
        Visible("MapRotationGroup", !removed);
        Visible("MapSizeGroup", kind == "Copy" || kind == "Move");
        Visible("MapPlacementGroup", !removed);
        Visible("MapAtPlayer", !removed);
        Visible("MapShapeGroup", false);
        Visible("MapOrderGroup", false);
        Visible("MapRebind", false);
        Visible("ScenePreviewGroup", catalog && selected);
        Visible("ScenePlaceGroup", catalog);
        Visible("SceneEditGroup", !catalog && selected && !removed);
        Visible("SceneRestoreGroup", !catalog && hasSavedPoint && (kind == "Move" || kind == "Hide" || kind == "Copy"));
        Visible("SceneRestore", kind != "Copy");
        Visible("SceneFocusGroup", !catalog && selected && !removed);
    }

    // Kept as an assembly compatibility entry point; all tools now float.
    public void ToggleDock(string id) => ShowPanel(id, true);

    public void ResetLayout()
    {
        var index = 0;
        foreach (var entry in _panels)
        {
            var panel = entry.Value;
            panel.Floating = panel.Drag.Movable = true;
            panel.Rect.SetParent(_dock, false);
            panel.Rect.anchorMin = panel.Rect.anchorMax = new Vector2(.5f, .5f);
            panel.Rect.pivot = new Vector2(.5f, .5f);
            panel.Rect.sizeDelta =
                entry.Key == "Library" ? new Vector2(440, 620)
                : entry.Key == "Inspector" ? new Vector2(380, 620)
                : entry.Key == "Controls" ? new Vector2(560, 270)
                : new Vector2(360, 580);
            panel.Rect.anchoredPosition =
                entry.Key == "Library" ? new Vector2(-_root.rect.width / 2 + 232, _root.rect.height / 2 - 410)
                : entry.Key == "Inspector" ? new Vector2(_root.rect.width / 2 - 202, _root.rect.height / 2 - 410)
                : new Vector2(index++ * 24, 0);
            panel.Visible = entry.Key == "Library" || entry.Key == "Inspector" && _selection.Length > 0;
        }
        DismissMenus();
        FitPanels();
        ApplyVisibility();
        LayoutChanged?.Invoke();
    }

    public void SetWalkthrough(bool active)
    {
        _walkthrough = active;
        DismissMenus();

        _controls["Workspace"].gameObject.SetActive(!active);

        _controls["EditorWalkStatus"].gameObject.SetActive(active);

        ApplyVisibility();
    }

    private void ApplyVisibility()
    {
        foreach (var entry in _panels)
        {
            entry.Value.Rect.gameObject.SetActive(entry.Value.Visible && !_walkthrough);
            if (entry.Value.Visible && !_walkthrough && !entry.Value.Shown)
            {
                entry.Value.Shown = true;
                Canvas.ForceUpdateCanvases();
                foreach (var scroll in entry.Value.Rect.GetComponentsInChildren<ScrollRect>(true))
                    scroll.verticalNormalizedPosition = 1;
            }
        }
    }

    public void KeepModalOnTop()
    {
        var shield = _controls["ConflictShield"];

        if (shield.gameObject.activeSelf)
        {
            DismissMenus();
            shield.SetAsLastSibling();
        }
    }

    // Called explicitly by SDK renders too; screen-camera previews use the render target's size.

    public void RefreshBounds()
    {
        var canvas = GetComponent<Canvas>();
        // Centered captions and restored window positions can land between pixels.
        // Snap uGUI text geometry to the display grid at every editor UI scale.
        canvas.pixelPerfect = true;

        var pixels = canvas.worldCamera && canvas.worldCamera.targetTexture ? canvas.worldCamera.targetTexture.height : Screen.height;

        GetComponent<CanvasScaler>().scaleFactor = Mathf.Max(1, pixels / 1080f);

        canvas.scaleFactor = Mathf.Max(1, pixels / 1080f);

        Canvas.ForceUpdateCanvases();

        _size = _root.rect.size;

        FitToolbar();
        FitPanels();
        ApplyVisibility();
        UpdateDetails();
        _theme.Refresh();
    }

    private void FitPanels()
    {
        foreach (var entry in _panels)
            FitPanel(entry.Key, entry.Value);
        FitContents();
    }

    private void LateUpdate()
    {
        _theme.Refresh();
        if (_root && _root.rect.size != _size)
            RefreshBounds();

        UpdateDetails();
        FitContents();
        KeepModalOnTop();
    }
}
