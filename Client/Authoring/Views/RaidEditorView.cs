using UnityEngine;
using UnityEngine.UIElements;
using WTT.Campaigns.Client.Authoring.Scenes;
using WTT.Campaigns.UI.Controls;
using ZLinq;

namespace WTT.Campaigns.Client.Authoring.Views;

internal sealed partial class RaidEditorView : IDisposable
{
    internal readonly EditorToolkitDocument Document;
    internal readonly EditorToolkitWindows Windows;
    internal GameObject Root => Document.Host;
    private readonly Dictionary<string, EditorControl> _controls = new(StringComparer.Ordinal);
    private readonly List<EditorInput> _inputs = new();
    private List<EditorButton> _rows => Browser.Rows;
    private bool _disposed;
    private string _context = "";
    private VisualElement? _choicePopup;
    private RouteOverlay _routeOverlay = null!;
    internal bool Valid => !_disposed && Root;
    internal bool PointerOver => Document.PointerOver;
    internal bool Typing =>
        Document.Typing
        || _dropdownDismissFrame == Time.frameCount
        || NumericDragging
        || Windows.Interacting
        || Windows.MenuDismissedThisFrame
        || _choicePopup != null;
    private bool NumericDragging
    {
        get
        {
            foreach (var input in _inputs)
                if (input.NumericDrag?.Active == true)
                    return true;
            return false;
        }
    }
    internal bool RowPressed
    {
        get
        {
            foreach (var browser in _browsers.Values)
            foreach (var row in browser.Rows)
                if (row.Pressed || browser.PointerHeld)
                    return true;
            return false;
        }
    }
    internal Shader PreviewShader => Document.PreviewShader;

    internal VisualElement Element(string name) => Control(name).Element;

    internal bool IsVisible(string name) => Control(name).Visible;

    internal T Get<T>(string name)
        where T : EditorControl => Control(name) as T ?? throw new InvalidOperationException("Wrong Editor control type: " + name);

    internal RaidEditorView()
    {
        Document = new EditorToolkitDocument("Campaign Editor", 32100);
        try
        {
            Build();
            Windows = new EditorToolkitWindows(this);
            EditorLayoutPreferences.Attach(Windows);
            BuildUsability();
            Document.Tick = () =>
            {
                Windows.Tick();
                PollCatalogCapacity();
                RefreshUsability();
            };
            Document.Escape = () =>
            {
                if (NumericDragging)
                {
                    CancelNumericDrags();
                    Document.EscapeFrame = Time.frameCount;
                }
                else if (Windows.Interacting)
                {
                    Windows.CancelInteraction();
                    Document.EscapeFrame = Time.frameCount;
                }
                else if (DismissDropdowns() || Windows.DismissMenus())
                    Document.EscapeFrame = Time.frameCount;
            };
            Document.CancelTyping = () =>
            {
                if (DismissDropdowns())
                    return;
                foreach (var input in _inputs)
                    if (input.isFocused)
                        input.CancelEdit();
            };
        }
        catch
        {
            Document.Dispose();
            throw;
        }
    }

    internal void SetVisible(bool visible)
    {
        if (!visible)
        {
            CancelNumericDrags();
            DismissDropdowns();
            Windows.CancelInteraction();
        }
        Document.SetVisible(visible);
    }

    internal void ReleaseFocus()
    {
        CancelNumericDrags();
        Document.ReleaseFocus();
    }

    private void CancelNumericDrags()
    {
        foreach (var input in _inputs)
            input.NumericDrag?.Cancel();
    }

    internal void Visible(string name, bool visible) => Control(name).Visible = visible;

    internal void Text(string name, string value) => Get<EditorLabel>(name).text = value;

    internal void Caption(string name, string value)
    {
        var button = Get<EditorButton>(name);
        button.Element.tooltip = button.Help.Length == 0 ? value : button.Help + "\nCurrent: " + value;
        if (!button.Element.ClassListContains("editor-icon-button"))
            button.text = value;
    }

    internal void Highlight(string name, bool selected) => Element(name).EnableInClassList("editor-selected", selected);

    internal bool IsChecked(string name) => ((Toggle)Element(name)).value;

    internal void Checked(string name, bool value) => ((Toggle)Element(name)).SetValueWithoutNotify(value);

    internal void Button(string name, Action action)
    {
        foreach (var (tool, control) in Matching(name))
            ((EditorButton)control).onClick.AddListener(() =>
            {
                if (Activate(tool))
                    action();
            });
    }

    internal void Input(string name, Action<string> action)
    {
        foreach (var (tool, control) in Matching(name))
            ((EditorInput)control).onEndEdit.AddListener(value =>
            {
                if (Activate(tool))
                    action(value);
            });
    }

    internal void Dropdown(string name, Action<int> action)
    {
        foreach (var (tool, control) in Matching(name))
            ((EditorChoice)control).onValueChanged.AddListener(value =>
            {
                if (Activate(tool))
                    action(value);
            });
    }

    internal void Value(string name, string value)
    {
        var input = Get<EditorInput>(name);
        if (!input.isFocused && !input.Invalid && input.text != value)
            input.SetTextWithoutNotify(value);
    }

    internal void SetDropdown(string name, List<EditorChoice.OptionData> options, int value)
    {
        var choice = Get<EditorChoice>(name);
        choice.options = options;
        choice.SetValueWithoutNotify(Mathf.Clamp(value, 0, Math.Max(0, options.Count - 1)));
    }

    internal void SetToolkitContext(string context)
    {
        if (_context == context)
            return;
        foreach (var input in _inputs)
            input.CancelEdit();
        DismissDropdowns();
        _context = context;
    }

    internal void Conflict(RaidEditorSession session)
    {
        var conflict = session.Conflict;
        Visible("ConflictShield", conflict != null);
        Windows.KeepModalOnTop();
        if (conflict == null)
            return;
        PresentConflicts(conflict);
    }

    internal void DrawRoute(WTT.Campaigns.Shared.Spatial.MapLayout? layout, Camera? camera, string selected, long layoutRevision = 0)
    {
        if (layout == null || !camera)
        {
            HideRoute();
            return;
        }
        _routeOverlay.style.display = DisplayStyle.Flex;
        _routeOverlay.Refresh(layout, camera!, selected, layoutRevision);
    }

    internal void HideRoute() => _routeOverlay.style.display = DisplayStyle.None;

    internal void InspectNavigation(bool enabled) => _routeOverlay.InspectNavigation = enabled;

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Windows.CancelInteraction();
        foreach (var input in _inputs)
            input.CancelEdit();
        EditorLayoutPreferences.Save(Windows);
        Document.Dispose();
    }
}
