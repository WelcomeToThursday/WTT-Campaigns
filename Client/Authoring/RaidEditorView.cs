using UnityEngine;
using UnityEngine.UIElements;
using WTT.Campaigns.UI.Controls;
using ZLinq;

namespace WTT.Campaigns.Client.Authoring;

internal sealed partial class RaidEditorView : IDisposable
{
    internal readonly EditorToolkitDocument Document;
    internal readonly EditorToolkitWindows Windows;
    internal GameObject Root => Document.Host;
    private readonly Dictionary<string, EditorControl> _controls = new(StringComparer.Ordinal);
    private readonly List<EditorInput> _inputs = new();
    private readonly List<EditorButton> _rows = new();
    private bool _disposed;
    private string _context = "";
    private VisualElement? _choicePopup;
    private RouteOverlay _routeOverlay = null!;
    internal bool Valid => !_disposed && Root;
    internal bool PointerOver => Document.PointerOver;
    internal bool Typing => Document.Typing || Windows.Interacting || _choicePopup != null;
    internal bool RowPressed
    {
        get
        {
            foreach (var row in _rows)
                if (row.Pressed)
                    return true;
            return false;
        }
    }
    internal Shader PreviewShader => Document.PreviewShader;

    internal VisualElement Element(string name) => _controls[name].Element;

    internal bool IsVisible(string name) => _controls[name].Visible;

    internal T Get<T>(string name)
        where T : EditorControl => _controls[name] as T ?? throw new InvalidOperationException("Wrong Editor control type: " + name);

    internal RaidEditorView()
    {
        Document = new EditorToolkitDocument("Campaign Editor", 32100);
        try
        {
            Build();
            Windows = new EditorToolkitWindows(this);
            EditorLayoutPreferences.Attach(Windows);
            Document.Tick = Windows.Tick;
            Document.Escape = () =>
            {
                if (DismissDropdowns() || Windows.DismissMenus())
                    Document.EscapeFrame = Time.frameCount;
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
            DismissDropdowns();
            Windows.CancelInteraction();
        }
        Document.SetVisible(visible);
    }

    internal void ReleaseFocus() => Document.ReleaseFocus();

    internal void Visible(string name, bool visible) => _controls[name].Visible = visible;

    internal void Text(string name, string value) => Get<EditorLabel>(name).text = value;

    internal void Caption(string name, string value)
    {
        var button = Get<EditorButton>(name);
        button.Element.tooltip = value;
        if (!button.Element.ClassListContains("editor-icon-button"))
            button.text = value;
    }

    internal void Highlight(string name, bool selected) => Element(name).EnableInClassList("editor-selected", selected);

    internal void Button(string name, Action action) => Get<EditorButton>(name).onClick.AddListener(() => action());

    internal void Input(string name, Action<string> action) => Get<EditorInput>(name).onEndEdit.AddListener(value => action(value));

    internal void Dropdown(string name, Action<int> action) => Get<EditorChoice>(name).onValueChanged.AddListener(value => action(value));

    internal void Value(string name, string value)
    {
        var input = Get<EditorInput>(name);
        if (!input.isFocused && input.text != value)
            input.SetTextWithoutNotify(value);
    }

    internal void SetDropdown(string name, List<EditorChoice.OptionData> options, int value)
    {
        var choice = Get<EditorChoice>(name);
        choice.options = options;
        choice.SetValueWithoutNotify(Mathf.Clamp(value, 0, Math.Max(0, options.Count - 1)));
    }

    internal bool DismissDropdowns()
    {
        if (_choicePopup == null)
            return false;
        _choicePopup.RemoveFromHierarchy();
        _choicePopup = null;
        return true;
    }

    private void OpenChoice(EditorChoice choice)
    {
        if (!choice.interactable)
            return;
        DismissDropdowns();
        var shield = new VisualElement();
        shield.AddToClassList("editor-popup-shield");
        _choicePopup = shield;
        Document.Content.Add(shield);
        shield.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.target == shield)
                DismissDropdowns();
        });
        var list = new ScrollView();
        EditorScrollStyle.Apply(list);
        list.AddToClassList("editor-choice-menu");
        var rect = choice.Element.worldBound;
        list.style.left = Mathf.Clamp(rect.x, 8, Document.Width - 308);
        list.style.top = Mathf.Clamp(rect.yMax, 8, Document.Height - 248);
        list.style.width = Mathf.Max(250, Mathf.Min(rect.width, Document.Width - 16));
        list.style.maxHeight = 240;
        shield.Add(list);
        for (var i = 0; i < choice.options.Count; i++)
        {
            var index = i;
            var button = new Button(() =>
            {
                DismissDropdowns();
                choice.SetValueWithoutNotify(index);
                choice.onValueChanged.Invoke(index);
            })
            {
                text = choice.options[i].text,
            };
            button.EnableInClassList("editor-selected", i == choice.value);
            list.Add(button);
        }
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
        Text("ConflictPath", conflict.Conflicts.AsValueEnumerable().Select(c => c.Path).JoinToString("\n"));
        Value("LocalConflict", conflict.Conflicts.AsValueEnumerable().Select(c => c.Local).JoinToString("\n\n"));
        Value("RemoteConflict", conflict.Conflicts.AsValueEnumerable().Select(c => c.Remote).JoinToString("\n\n"));
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
