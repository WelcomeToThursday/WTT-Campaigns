using UnityEngine;
using UnityEngine.UIElements;
using WTT.Campaigns.UI.Controls;
using ZLinq;

namespace WTT.Campaigns.Client.Authoring.Views;

internal sealed partial class EditorToolkitWindows
{
    private readonly RaidEditorView _view;
    private readonly Dictionary<string, EditorWindowPlacement> _panels = new();
    private readonly Dictionary<string, string> _tips = new();
    private readonly Label _tooltip = new() { pickingMode = PickingMode.Ignore, enableRichText = false };
    private VisualElement? _tooltipAnchor;
    private string _selection = "",
        _category = "";
    private bool _walkthrough,
        _capture,
        _recordDetails,
        _mapDetails,
        _recordAvailable,
        _identityAvailable;
    private Vector2 _size;
    private VisualElement? _dragHandle;
    private string _dragId = "";
    private int _pointerId;
    private bool _resize;
    private Vector2 _start;
    private EditorWindowPlacement? _before;
    internal Action? LayoutChanged;
    internal bool Interacting => _dragHandle != null;
    internal bool HasMenu => _view.IsVisible("WindowsMenu") || _view.IsVisible("ContextMenu");

    internal EditorToolkitWindows(RaidEditorView view)
    {
        _view = view;
        foreach (var id in new[] { "Library", "Inspector", "EnvironmentMenu", "Controls" })
        {
            _panels.Add(id, new EditorWindowPlacement { Id = id });
            BindDrag(id, view.Element(id + "TitleBar"), false);
            BindDrag(id, view.Element(id + "Resize"), true);
            view.Element(id).RegisterCallback<PointerDownEvent>(_ => Focus(id), TrickleDown.TrickleDown);
        }
        Bind("LibraryCollapse", () => ShowPanel("Library", false));
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
        _tooltip.AddToClassList("editor-tooltip");
        _view.Document.Content.Add(_tooltip);
        _tooltip.RegisterCallback<GeometryChangedEvent>(_ => PlaceTooltip());
        HideTooltip();
        ResetLayout();
    }

    private void Bind(string id, Action action) => _view.Button(id, action);

    private void Visible(string id, bool visible) => _view.Visible(id, visible);

    private void Focus(string id)
    {
        if (_view.IsVisible("ConflictShield"))
            return;
        _view.Element(id).BringToFront();
        _view.Element("WindowsMenu").BringToFront();
        _view.Element("ContextMenu").BringToFront();
        KeepModalOnTop();
    }

    internal void ShowPanel(string id, bool visible)
    {
        _panels[id].Visible = visible;
        ApplyVisibility();
        if (visible)
            Focus(id);
        LayoutChanged?.Invoke();
    }

    private void ToggleWindow(string id)
    {
        DismissMenus();
        ShowPanel(id, !_panels[id].Visible);
    }

    private void ToggleMenu(string id)
    {
        var show = !_view.IsVisible(id);
        DismissMenus();
        Visible(id, show);
        _view.Element(id).BringToFront();
    }

    internal bool DismissMenus()
    {
        var shown = HasMenu;
        Visible("WindowsMenu", false);
        Visible("ContextMenu", false);
        HideTooltip();
        return shown;
    }

    internal void KeepModalOnTop()
    {
        if (!_view.IsVisible("ConflictShield"))
            return;
        DismissMenus();
        _view.DismissDropdowns();
        _view.Element("ConflictShield").BringToFront();
    }

    internal void SetTooltip(string id, string text)
    {
        _tips[id] = text;
        _view.Element(id).tooltip = text;
        if (_view.Element(id) is Label)
            _view.Element(id).pickingMode = PickingMode.Position;
    }

    internal void ShowTooltip(string id, string fallback, VisualElement? anchor = null)
    {
        if (_walkthrough || _view.IsVisible("ConflictShield"))
            return;
        var text = _tips.GetValueOrDefault(id, fallback);
        if (string.IsNullOrWhiteSpace(text))
        {
            HideTooltip();
            return;
        }
        _tooltipAnchor = anchor ?? _view.Element(id);
        _tooltip.text = text;
        _tooltip.style.maxWidth = Math.Min(360, _view.Document.Width - 16);
        _tooltip.style.visibility = Visibility.Hidden;
        _tooltip.style.display = DisplayStyle.Flex;
        _tooltip.BringToFront();
    }

    private void PlaceTooltip()
    {
        if (_tooltip.style.display.value == DisplayStyle.None)
            return;
        if (_tooltipAnchor?.panel == null)
        {
            HideTooltip();
            return;
        }
        var size = _tooltip.layout.size;
        if (!float.IsFinite(size.x) || !float.IsFinite(size.y) || size.x <= 0 || size.y <= 0)
            return;
        // Element bounds and the tooltip parent share panel coordinates. Using
        // the measured width avoids reserving 360px for a short toolbar hint.
        var parent = _view.Document.Content;
        var min = parent.WorldToLocal(_tooltipAnchor.worldBound.min);
        var max = parent.WorldToLocal(_tooltipAnchor.worldBound.max);
        var position = EditorTooltipPlacement.Place(
            min.x,
            max.x,
            min.y,
            max.y,
            size.x,
            size.y,
            _view.Document.Width,
            _view.Document.Height
        );
        _tooltip.style.left = position.X;
        _tooltip.style.top = position.Y;
        _tooltip.style.visibility = Visibility.Visible;
    }

    internal void HideTooltip()
    {
        _tooltipAnchor = null;
        _tooltip.style.display = DisplayStyle.None;
    }

    internal void BrowseCategory() => ShowPanel("Library", true);

    internal void Select(string category, string selection)
    {
        if (_category == category && _selection == selection)
            return;
        var changed = _category != category;
        _category = category;
        _selection = selection;
        ShowPanel("Inspector", selection.Length > 0);
        if (changed && selection.Length == 0)
            ShowPanel("Library", true);
        ((ScrollView)_view.Element("PropertyScroll")).scrollOffset = Vector2.zero;
    }

    internal void SetWalkthrough(bool active)
    {
        _walkthrough = active;
        CancelInteraction();
        DismissMenus();
        Visible("Workspace", !active);
        Visible("EditorWalkStatus", active);
        ApplyVisibility();
    }

    private void ApplyVisibility()
    {
        foreach (var pair in _panels)
            Visible(pair.Key, pair.Value.Visible && !_walkthrough);
    }

    private void FitContents() { }

    private void FitPanels()
    {
        foreach (var id in new[] { "Library", "Inspector", "EnvironmentMenu", "Controls" })
            Apply(id, _panels[id]);
    }

    private Vector2 Minimum(string id) =>
        id == "Library" ? new Vector2(400, 480)
        : id == "Controls" ? new Vector2(440, 220)
        : new Vector2(360, 320);

    private void Apply(string id, EditorWindowPlacement placement)
    {
        var doc = _view.Document;
        var min = Minimum(id);
        var fit = EditorWindowPlacement.Fit(placement, doc.Width, doc.Height, min.x, min.y, _capture ? 88 : 40);
        _panels[id] = fit;
        var style = _view.Element(id).style;
        style.left = doc.Width / 2 + fit.X * doc.Width - fit.Width / 2;
        style.top = doc.Height / 2 - fit.Y * doc.Height - fit.Height / 2;
        style.width = fit.Width;
        style.height = fit.Height;
    }

    internal EditorWindowLayout CaptureLayout() => new() { Windows = _panels.Values.AsValueEnumerable().ToArray() };

    internal void RestoreLayout(EditorWindowLayout? layout)
    {
        if (layout?.Version != 1 || layout.Windows == null)
            return;
        foreach (var placement in layout.Windows)
            if (placement != null && _panels.ContainsKey(placement.Id))
                Apply(placement.Id, placement);
        ApplyVisibility();
    }

    internal void ResetLayout()
    {
        var doc = _view.Document;
        foreach (var id in new[] { "Library", "Inspector", "EnvironmentMenu", "Controls" })
        {
            var width =
                id == "Library" ? 440
                : id == "Inspector" ? 380
                : id == "Controls" ? 560
                : 360;
            Apply(
                id,
                new EditorWindowPlacement
                {
                    Id = id,
                    Width = width,
                    Height = id == "Controls" ? 290 : 620,
                    X =
                        id == "Library" ? (-doc.Width / 2 + 232) / doc.Width
                        : id == "Inspector" ? (doc.Width / 2 - 202) / doc.Width
                        : 0,
                    Y = (doc.Height / 2 - 410) / doc.Height,
                    Visible = id == "Library" || id == "Inspector" && _selection.Length > 0,
                }
            );
        }
        DismissMenus();
        ApplyVisibility();
        LayoutChanged?.Invoke();
    }

    private void BindDrag(string id, VisualElement handle, bool resize)
    {
        handle.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.button != 0 || evt.target is Button || _view.IsVisible("ConflictShield"))
                return;
            _dragId = id;
            _resize = resize;
            _dragHandle = handle;
            _pointerId = evt.pointerId;
            _start = evt.position;
            _before = _panels[id];
            handle.CapturePointer(evt.pointerId);
            Focus(id);
            evt.StopPropagation();
        });
        handle.RegisterCallback<PointerMoveEvent>(evt =>
        {
            if (_dragHandle != handle || _before == null)
                return;
            var delta = (Vector2)evt.position - _start;
            var doc = _view.Document;
            Apply(
                id,
                new EditorWindowPlacement
                {
                    Id = id,
                    Visible = true,
                    Width = _before.Width + (_resize ? delta.x : 0),
                    Height = _before.Height + (_resize ? delta.y : 0),
                    X = _before.X + delta.x / doc.Width * (_resize ? .5f : 1),
                    Y = _before.Y - delta.y / doc.Height * (_resize ? .5f : 1),
                }
            );
            evt.StopPropagation();
        });
        handle.RegisterCallback<PointerUpEvent>(evt =>
        {
            if (_dragHandle == handle)
            {
                CancelInteraction();
                LayoutChanged?.Invoke();
                evt.StopPropagation();
            }
        });
        handle.RegisterCallback<PointerCaptureOutEvent>(_ =>
        {
            if (_dragHandle == handle)
            {
                _dragHandle = null;
                _before = null;
            }
        });
    }

    internal void CancelInteraction()
    {
        var handle = _dragHandle;
        _dragHandle = null;
        _before = null;
        if (handle != null && handle.HasPointerCapture(_pointerId))
            handle.ReleasePointer(_pointerId);
    }

    internal void Tick()
    {
        var size = new Vector2(_view.Document.Width, _view.Document.Height);
        if (_size != size)
        {
            _size = size;
            FitPanels();
        }
        UpdateDetails();
        KeepModalOnTop();
        if (_tooltip.style.display.value != DisplayStyle.None)
            PlaceTooltip();
    }
}
