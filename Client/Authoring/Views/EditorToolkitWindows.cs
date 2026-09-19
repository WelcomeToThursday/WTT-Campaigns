using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UIElements;
using WTT.Campaigns.UI.Controls;
using ZLinq;

namespace WTT.Campaigns.Client.Authoring.Views;

internal sealed partial class EditorToolkitWindows
{
    private readonly RaidEditorView _view;
    private readonly Dictionary<string, EditorWindowPlacement> _panels = new();
    private readonly Label _tooltip;
    private VisualElement? _tooltipAnchor;
    private string _selection = "",
        _category = "";
    private bool _walkthrough,
        _capture,
        _recordDetails,
        _mapDetails,
        _recordAvailable,
        _identityAvailable;
    private string _detailContext = "";

    internal void DetailContext(string context)
    {
        if (_detailContext == context)
            return;
        _detailContext = context;
        _recordDetails = EditorLayoutPreferences.Expanded(context, "Record details", false);
        _mapDetails = EditorLayoutPreferences.Expanded(context, "Map details", false);
        UpdateDetails();
    }

    private Vector2 _size;
    private int _scalePercent;
    private EditorDockNode _dock = EditorDockNode.Default();
    private readonly VisualElement _chrome;
    private readonly VisualElement _dropPreview;
    private readonly Dictionary<string, VisualElement> _bars = new(),
        _dividers = new();
    private readonly Dictionary<string, Rect> _windowBounds = new();
    private Dictionary<string, EditorDockRect> _dockRects = new();
    private readonly HashSet<string> _sized = new(),
        _opened = new();
    private VisualElement? _dragHandle;
    private string _dragId = "";
    private int _pointerId;
    private bool _resize,
        _dragMoved;
    private Vector2 _start;
    private Rect _before;
    private EditorWindowLayout? _dragLayout;
    private EditorDockNode? _candidate;
    internal Action? LayoutChanged;
    internal bool Interacting => _dragHandle != null;
    internal bool HasMenu => _view.IsVisible("WindowsMenu") || _view.IsVisible("ContextMenu");
    internal bool Modal => _view.IsVisible("ConflictShield");
    internal string ActiveTool = "Layouts";
    internal bool LayoutRestored { get; private set; }

    internal EditorToolkitWindows(RaidEditorView view)
    {
        _view = view;
        _tooltip = view.Document.Clone<Label>("Tooltip");
        _chrome = view.Document.Clone<VisualElement>("Workspace");
        _dropPreview = view.Document.Clone<VisualElement>("DropPreview");
        foreach (
            var id in RaidEditorView
                .ToolIds.AsValueEnumerable()
                .Select(t => "Tool:" + t)
                .Concat(new[] { "Inspector", "EnvironmentMenu", "Controls", "LootConfiguration" })
        )
        {
            _panels.Add(id, new() { Id = id });
            BindDrag(id, view.WindowElement(id, "TitleBar"), false);
            BindDrag(id, view.WindowElement(id, "Resize"), true);
            view.WindowElement(id).RegisterCallback<PointerDownEvent>(_ => Focus(id), TrickleDown.TrickleDown);
            var window = view.WindowElement(id);
            Action fitFields = () =>
            {
                var narrow = window.layout.width < 340;
                // Rows own their wrapping policy. Resizing must preserve compact pairs such as paging.
                foreach (var field in window.Query<TextField>().ToList())
                    EditorControlLayout.Field(field, narrow);
            };
            window.RegisterCallback<GeometryChangedEvent>(_ => _view.AfterLayout(fitFields));
        }
        Bind("LibraryCollapse", () => ShowPanel("Library", false));
        Bind("LootClose", () => ShowPanel("LootConfiguration", false));
        Bind("LootTool", () => ToggleWindow("LootConfiguration"));
        Bind("LootWindowToggle", () => ToggleWindow("LootConfiguration"));
        Bind("InspectorCollapse", () => ShowPanel("Inspector", false));
        Bind("EnvironmentClose", () => ShowPanel("EnvironmentMenu", false));
        Bind("HelpClose", () => ShowPanel("Controls", false));
        Bind("LibraryToggle", () => ToggleWindow("Library"));
        Bind("InspectorToggle", () => ToggleWindow("Inspector"));
        Bind("EnvironmentToggle", () => ToggleWindow("EnvironmentMenu"));
        Bind("EnvironmentWindowToggle", () => ToggleWindow("EnvironmentMenu"));
        Bind("HelpToggle", () => ToggleWindow("Controls"));
        Bind("HelpWindowToggle", () => ToggleWindow("Controls"));
        Bind("WindowsToggle", () => ToggleMenu("WindowsMenu"));
        Bind("ContextToggle", () => ToggleMenu("ContextMenu"));
        Bind("ResetLayout", ResetLayout);
        Bind("UiSizeSmaller", () => EditorLayoutPreferences.SetScale(EditorLayoutPreferences.ScalePercent - 5));
        Bind("UiSizeLarger", () => EditorLayoutPreferences.SetScale(EditorLayoutPreferences.ScalePercent + 5));
        Bind("UiSizeReset", () => EditorLayoutPreferences.SetScale(EditorUiScale.DefaultPercent));
        Bind(
            "RecordDetailsToggle",
            () =>
            {
                _recordDetails = !_recordDetails;
                EditorLayoutPreferences.SetExpanded(_detailContext, "Record details", _recordDetails);
                UpdateDetails();
            }
        );
        Bind(
            "MapDetailsToggle",
            () =>
            {
                _mapDetails = !_mapDetails;
                EditorLayoutPreferences.SetExpanded(_detailContext, "Map details", _mapDetails);
                UpdateDetails();
            }
        );
        _view.Element("Workspace").Add(_chrome);
        _dropPreview.style.display = DisplayStyle.None;
        _view.Element("Workspace").Add(_dropPreview);
        _tooltip.AddToClassList("editor-tooltip");
        _view.Document.Content.Add(_tooltip);
        _tooltip.RegisterCallback<GeometryChangedEvent>(_ => _view.AfterLayout(PlaceTooltip));
        HideTooltip();
        SetupMenus();
        ResetLayout();
    }

    private void Bind(string id, Action action) => _view.Button(id, action);

    private void Visible(string id, bool visible) => _view.Visible(id, visible);

    private string Resolve(string id) => id == "Library" ? "Tool:" + _view.ToolContext : id;

    internal bool IsOpen(string id) => _panels.TryGetValue(Resolve(id), out var p) && p.Visible;

    private bool AllowedPanel(string id) => !id.StartsWith("Tool:") || _view.AllowsTool(id.Substring(5));

    private HashSet<string> OpenIds() =>
        _panels.AsValueEnumerable().Where(p => p.Value.Visible && AllowedPanel(p.Key)).Select(p => p.Key).ToHashSet();

    private EditorDockRect Area =>
        new(56, 84, Math.Max(1, _view.Document.Width - 64), Math.Max(1, _view.Document.Height - 84 - (_capture ? 88 : 40)));

    private void Focus(string id)
    {
        if (Modal)
            return;
        id = Resolve(id);
        _view.WindowElement(id).BringToFront();
        _chrome.BringToFront();
        foreach (var p in _panels.AsValueEnumerable().Where(p => p.Value.Visible && !Docked(p.Key)))
            _view.WindowElement(p.Key).BringToFront();
        if (!Docked(id))
            _view.WindowElement(id).BringToFront();
        _view.Element("CategoryRail").BringToFront();
        _view.Element("WindowsMenu").BringToFront();
        _view.Element("ContextMenu").BringToFront();
        KeepModalOnTop();
    }

    internal void ShowPanel(string id, bool visible)
    {
        id = Resolve(id);
        if (visible && !AllowedPanel(id))
            return;
        _panels[id].Visible = visible;
        if (visible)
        {
            _panels[id].Opened = true;
            _opened.Add(id);
        }
        if (visible)
        {
            var group = EditorDockLayout.Nodes(_dock).AsValueEnumerable().FirstOrDefault(n => n.Tabs.AsValueEnumerable().Contains(id));
            if (group != null)
                group.Active = id;
        }
        RebuildChrome();
        FitPanels();
        if (visible)
            Focus(id);
        LayoutChanged?.Invoke();
    }

    private void ToggleWindow(string id)
    {
        DismissMenus();
        ShowPanel(id, !IsOpen(id));
    }

    private void ToggleMenu(string id)
    {
        var show = !_view.IsVisible(id);
        DismissMenus();
        Visible(id, show);
        if (show)
            OpenMenu(id);
    }

    internal bool DismissMenus()
    {
        var shown = HasMenu;
        if (shown)
            _menuDismissFrame = Time.frameCount;
        _openMenu = "";
        _menuShield.style.display = DisplayStyle.None;
        Visible("WindowsMenu", false);
        Visible("ContextMenu", false);
        HideTooltip();
        return shown;
    }

    internal void KeepModalOnTop()
    {
        if (!Modal)
            return;
        if (Interacting)
            CancelInteraction();
        DismissMenus();
        _view.DismissDropdowns();
        _view.Element("ConflictShield").BringToFront();
    }

    internal void BrowseCategory()
    {
        ActiveTool = _view.ToolContext;
        var id = "Tool:" + ActiveTool;
        if (!Docked(id) && !_opened.Contains(id))
        {
            var target = EditorDockLayout
                .Nodes(_dock)
                .AsValueEnumerable()
                .FirstOrDefault(n => n.Kind == "tabs" && n.Tabs.AsValueEnumerable().Any(t => t.StartsWith("Tool:")));
            if (target != null)
                _dock = EditorDockLayout.Dock(_dock, id, target.Id, "center");
        }
        _opened.Add(id);
        ShowPanel(id, true);
        FitContents();
    }

    internal void Select(string category, string selection)
    {
        ActiveTool = category == "Maps" ? "Layouts" : category.Split('/')[0];
        if (_category == category && _selection == selection)
            return;
        _category = category;
        _selection = selection;
        if (selection.Length > 0 && !IsOpen("Inspector"))
            ShowPanel("Inspector", true);
        ((ScrollView)_view.Element("PropertyScroll")).scrollOffset = Vector2.zero;
    }

    internal void SetWalkthrough(bool active)
    {
        CancelInteraction();
        _walkthrough = active;
        DismissMenus();
        Visible("Workspace", !active);
        Visible("EditorWalkStatus", active);
        FitPanels();
    }

    private bool Docked(string id) => EditorDockLayout.Nodes(_dock).AsValueEnumerable().Any(n => n.Tabs.AsValueEnumerable().Contains(id));

    internal void FitContents()
    {
        var id = "Tool:" + _view.ToolContext;
        if (!_panels.TryGetValue(id, out var p) || p.ManualSize || _sized.Contains(id))
            return;
        var count = Math.Max(_view.TreeVisibleCount, _view.VisibleRowCount);
        p.Height = Math.Clamp(150 + Math.Min(12, Math.Max(1, count)) * 28 + (_view.ToolContext == "AI" ? 170 : 50), 180, 620);
        if (count > 0)
            _sized.Add(id);
        FitPanels();
    }

    private void FitPanels()
    {
        var area = Area;
        var open = OpenIds();
        _dockRects = EditorDockLayout.Arrange(_dock, area, open);
        foreach (var pair in _panels)
        {
            var id = pair.Key;
            var p = pair.Value;
            var group = EditorDockLayout.Nodes(_dock).AsValueEnumerable().FirstOrDefault(n => n.Tabs.AsValueEnumerable().Contains(id));
            var visible = p.Visible && !_walkthrough && AllowedPanel(id);
            Rect rect;
            if (group != null)
            {
                var active =
                    group.Tabs.AsValueEnumerable().Contains(group.Active) && open.Contains(group.Active)
                        ? group.Active
                        : group.Tabs.AsValueEnumerable().FirstOrDefault(t => open.Contains(t));
                visible &= active == id;
                var r = _dockRects.GetValueOrDefault(group.Id);
                rect = new(r.X, r.Y + EditorDockLayout.TabHeight, r.Width, Math.Max(0, r.Height - EditorDockLayout.TabHeight));
            }
            else
            {
                var width = Math.Clamp(float.IsFinite(p.Width) ? p.Width : 360, Math.Min(280, area.Width), area.Width);
                var height = Math.Clamp(float.IsFinite(p.Height) ? p.Height : 360, Math.Min(180, area.Height), area.Height);
                var x = _view.Document.Width * (.5f + (float.IsFinite(p.X) ? p.X : 0)) - width / 2;
                var y = _view.Document.Height * (.5f - (float.IsFinite(p.Y) ? p.Y : 0)) - height / 2;
                rect = new(
                    Math.Clamp(x, area.X, area.X + area.Width - width),
                    Math.Clamp(y, area.Y, area.Y + area.Height - height),
                    width,
                    height
                );
            }
            _windowBounds[id] = rect;
            var window = _view.WindowElement(id);
            window.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            Place(window, rect);
            _view.WindowElement(id, "Resize").style.display = group == null ? DisplayStyle.Flex : DisplayStyle.None;
            _view.WindowElement(id, "TitleBar").style.height = 28;
        }
        foreach (var node in EditorDockLayout.Nodes(_dock))
        {
            if (!_dockRects.TryGetValue(node.Id, out var r))
                continue;
            if (node.Kind == "viewport")
                _view.SetViewport(r);
            if (_bars.TryGetValue(node.Id, out var bar))
                Place(bar, new(r.X, r.Y, r.Width, EditorDockLayout.TabHeight));
            if (
                _dividers.TryGetValue(node.Id, out var divider)
                && node.First != null
                && node.Second != null
                && _dockRects.TryGetValue(node.First.Id, out var a)
                && _dockRects.ContainsKey(node.Second.Id)
            )
                Place(divider, node.Kind == "horizontal" ? new(a.X + a.Width, r.Y, 6, r.Height) : new(r.X, a.Y + a.Height, r.Width, 6));
        }
        foreach (var tool in RaidEditorView.ToolIds)
        {
            var button = _view.Element(tool);
            button.EnableInClassList("editor-open-tool", IsOpen("Tool:" + tool));
            _view.Highlight(tool, ActiveTool == tool);
        }
    }

    private static void Place(VisualElement element, Rect r)
    {
        element.style.position = Position.Absolute;
        element.style.left = r.x;
        element.style.top = r.y;
        element.style.width = r.width;
        element.style.height = r.height;
    }

    internal EditorWindowLayout CaptureLayout() => new() { Windows = _panels.Values.AsValueEnumerable().ToArray(), Dock = _dock };

    private static T Clone<T>(T source) => JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(source))!;

    internal void RestoreLayout(EditorWindowLayout? layout)
    {
        var restored = EditorWindowLayout.Restore(layout, CaptureLayout());
        if (restored == null)
            return;
        LayoutRestored = true;
        _dock = restored.Dock!;
        foreach (var placement in restored.Windows)
        {
            _panels[placement.Id] = placement;
            if (placement.ManualSize)
                _sized.Add(placement.Id);
            if (placement.Opened || placement.Visible || placement.ManualSize || Docked(placement.Id))
                _opened.Add(placement.Id);
        }
        RebuildChrome();
        FitPanels();
    }

    internal void ResetLayout()
    {
        CancelInteraction();
        _dock = EditorDockNode.Default();
        _sized.Clear();
        _opened.Clear();
        foreach (var id in _panels.Keys.AsValueEnumerable().ToArray())
            _panels[id] = new()
            {
                Id = id,
                Width =
                    id == "LootConfiguration" ? 460
                    : id is "Tool:Scene" or "Tool:AI" ? 420
                    : 360,
                Height = id == "LootConfiguration" ? 620 : 400,
                X = -.15f,
                Y = .05f,
                Visible = id == "Tool:Layouts" || id == "Inspector" && _selection.Length > 0,
            };
        DismissMenus();
        RebuildChrome();
        FitPanels();
        LayoutChanged?.Invoke();
    }

    internal void Tick()
    {
        if (_scalePercent != EditorLayoutPreferences.ScalePercent)
        {
            _scalePercent = EditorLayoutPreferences.ScalePercent;
            _view.Document.ScalePercent = _scalePercent;
            _view.Get<EditorLabel>("UiSizeLabel").text = $"UI size: {_scalePercent}%";
            _view.Element("UiSizeSmaller").SetEnabled(_scalePercent > EditorUiScale.MinimumPercent);
            _view.Element("UiSizeLarger").SetEnabled(_scalePercent < EditorUiScale.MaximumPercent);
        }
        var size = new Vector2(_view.Document.Width, _view.Document.Height);
        if (_size != size)
        {
            if (Interacting)
                CancelInteraction();
            _size = size;
            RebuildChrome();
            FitPanels();
        }
        UpdateDetails();
        KeepModalOnTop();
        if (_openMenu.Length > 0)
            PositionMenu();
        if (_tooltip.style.display.value != DisplayStyle.None)
            PlaceTooltip();
    }
}
