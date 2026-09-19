using UnityEngine;
using UnityEngine.UIElements;
using WTT.Campaigns.Client.Authoring.Console;

namespace WTT.Campaigns.Client.Authoring.Views;

internal sealed class EditorConsoleView
{
    private readonly RaidEditorView _view;
    private readonly ListView _list;
    private readonly TextField _input,
        _search,
        _details;
    private readonly Foldout _detailsFold;
    private readonly Label _hint,
        _counts;
    private readonly Toggle _all,
        _info,
        _warning,
        _error,
        _debug,
        _scroll;
    private readonly List<ConsoleEntry> _visible = new();
    private EditorConsoleListener? _listener;
    private EditorConsoleCommands? _commands;
    private ConsoleEntry? _selected;
    private long _version = -1;
    private float _nextRefresh;
    private string[] _completions = Array.Empty<string>();
    private int _completionIndex;
    private bool _suggestionsDismissed;
    private int _textSize;
    private readonly ScrollView _outputScroll;

    internal EditorConsoleView(RaidEditorView view)
    {
        _view = view;
        var root = view.Element("Console");
        var toolbar = root.Q<ScrollView>("ConsoleToolbarScroll");
        toolbar.horizontalScrollerVisibility = ScrollerVisibility.Auto;
        toolbar.verticalScrollerVisibility = ScrollerVisibility.Hidden;
        _list = root.Q<ListView>("ConsoleMessages");
        _input = root.Q<TextField>("ConsoleCommand");
        _search = root.Q<TextField>("ConsoleSearch");
        _details = root.Q<TextField>("ConsoleDetails");
        _detailsFold = root.Q<Foldout>("ConsoleDetailsFold");
        _hint = root.Q<Label>("ConsoleHint");
        _counts = root.Q<Label>("ConsoleCounts");
        _all = root.Q<Toggle>("ConsoleAll");
        _info = root.Q<Toggle>("ConsoleInfo");
        _warning = root.Q<Toggle>("ConsoleWarning");
        _error = root.Q<Toggle>("ConsoleError");
        _debug = root.Q<Toggle>("ConsoleDebug");
        _scroll = root.Q<Toggle>("ConsoleScroll");
        _list.itemsSource = _visible;
        _list.virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight;
        _list.horizontalScrollingEnabled = false;
        _list.selectionType = SelectionType.Single;
        _list.makeItem = () =>
        {
            var row = view.Document.Clone<VisualElement>("ConsoleRow");
            row.Q<Label>("ConsoleTime").enableRichText = false;
            row.Q<Label>("ConsoleMessage").enableRichText = false;
            return row;
        };
        _list.bindItem = (element, index) =>
        {
            var entry = _visible[index];
            // Wrapped, variable-height rows expose the complete retained message, including command output.
            element.Q<Label>("ConsoleTime").text = $"{entry.Time:HH:mm:ss}";
            element.Q<Label>("ConsoleMessage").text = $"[{entry.Severity}] [{entry.Source}] {entry.Message}";
            element.EnableInClassList("editor-console-info", entry.Severity == ConsoleSeverity.Info);
            element.EnableInClassList("editor-console-debug", entry.Severity == ConsoleSeverity.Debug);
            element.EnableInClassList("editor-console-error", entry.Severity == ConsoleSeverity.Error);
            element.EnableInClassList("editor-console-warning", entry.Severity == ConsoleSeverity.Warning);
            element.EnableInClassList("editor-console-command", entry.Command);
        };
        _list.selectionChanged += items =>
        {
            _selected = null;
            foreach (var item in items)
            {
                _selected = item as ConsoleEntry;
                break;
            }
            _details.SetValueWithoutNotify(_selected?.Text ?? "Select a message to see its full text.");
        };
        _list.itemsChosen += _ => _detailsFold.value = true;
        foreach (var toggle in new[] { _all, _info, _warning, _error, _debug })
            toggle.RegisterValueChangedCallback(_ => Invalidate());
        _search.RegisterValueChangedCallback(_ => Invalidate());
        root.Q<Button>("ConsoleTextSmaller").clicked += () => SetTextSize(_textSize - 1);
        root.Q<Button>("ConsoleTextLarger").clicked += () => SetTextSize(_textSize + 1);
        root.Q<Button>("ConsoleTextReset").clicked += () => SetTextSize(EditorConsolePreferences.DefaultTextSize);
        root.Q<Button>("ConsoleClear").clicked += () => _listener?.Buffer.Clear();
        root.Q<Button>("ConsoleCopy").clicked += () =>
        {
            if (_selected != null)
                GUIUtility.systemCopyBuffer = _selected.Text;
        };
        _input.maxLength = 2048;
        _search.maxLength = 256;
        _input.RegisterValueChangedCallback(_ =>
        {
            _completions = Array.Empty<string>();
            _suggestionsDismissed = false;
            UpdateHint();
        });
        _input.RegisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
        _hint.text = "Type help to list commands.";
        _outputScroll = _list.Q<ScrollView>();
        EditorScrollStyle.Apply(_outputScroll);
        _outputScroll.verticalScrollerVisibility = ScrollerVisibility.AlwaysVisible;
        _outputScroll.mouseWheelScrollSize = 40;
        // Keep manual reading stable while new messages arrive. The toggle explicitly resumes following.
        _list.RegisterCallback<WheelEvent>(
            evt =>
            {
                if (evt.delta.y < 0)
                    _scroll.value = false;
            },
            TrickleDown.TrickleDown
        );
        _outputScroll.verticalScroller.RegisterCallback<PointerDownEvent>(_ => _scroll.value = false, TrickleDown.TrickleDown);
        _list.RegisterCallback<KeyDownEvent>(
            evt =>
            {
                if (evt.keyCode is KeyCode.PageUp or KeyCode.Home or KeyCode.UpArrow)
                    _scroll.value = false;
            },
            TrickleDown.TrickleDown
        );
        _scroll.RegisterValueChangedCallback(evt =>
        {
            if (evt.newValue)
                FollowOutput();
        });
        ApplyTextSize();
    }

    private void SetTextSize(int size)
    {
        EditorConsolePreferences.SetTextSize(size);
        ApplyTextSize();
    }

    private void ApplyTextSize()
    {
        var size = EditorConsolePreferences.TextSize;
        if (size == _textSize)
            return;
        var root = _view.Element("Console");
        root.RemoveFromClassList("editor-console-size-" + _textSize);
        _textSize = size;
        root.AddToClassList("editor-console-size-" + _textSize);
        root.Q<Label>("ConsoleTextSize").text = "Text: " + _textSize;
        root.Q<Button>("ConsoleTextSmaller").SetEnabled(_textSize > EditorConsolePreferences.MinimumTextSize);
        root.Q<Button>("ConsoleTextLarger").SetEnabled(_textSize < EditorConsolePreferences.MaximumTextSize);
        // Invalidate measured heights after a user changes font size; retained messages and history stay intact.
        _list.Rebuild();
        if (_scroll.value)
            FollowOutput();
    }

    private void FollowOutput()
    {
        _list.schedule.Execute(() =>
        {
            if (_scroll.value && _visible.Count > 0 && _view.Document.Visible && _view.Windows.IsOpen("Console"))
                _list.ScrollToItem(-1);
        });
    }

    internal void Bind(EditorConsoleListener listener, EditorConsoleCommands commands)
    {
        Unbind();
        _listener = listener;
        _commands = commands;
        listener.Buffer.Add("Console", "Type help to list editor commands. Double-click a message to expand its details.", command: true);
        UpdateHint();
    }

    internal void Unbind()
    {
        _listener = null;
        _commands = null;
        _visible.Clear();
        _selected = null;
        _input.SetValueWithoutNotify("");
        _details.SetValueWithoutNotify("");
        _completions = Array.Empty<string>();
        Invalidate();
    }

    private void Invalidate() => _version = -1;

    private void UpdateHint()
    {
        if (_commands == null)
            return;
        var suggestions = _commands.Complete(_input.value);
        _hint.text = _commands.Hint(_input.value);
        if (!_suggestionsDismissed && _input.value.Length > 0 && suggestions.Length > 0)
            _hint.text += "\n" + string.Join("  ·  ", suggestions);
    }

    internal bool CancelTyping()
    {
        var focused = _view.Document.Root.panel?.focusController.focusedElement as VisualElement;
        if (focused != _input && (focused == null || !_input.Contains(focused)))
        {
            if (focused == null || !_view.Element("Console").Contains(focused))
                return false;
            _view.Document.ReleaseFocus();
            return true;
        }
        if (!_suggestionsDismissed && _commands?.Complete(_input.value).Length > 0 && _input.value.Length > 0)
        {
            _suggestionsDismissed = true;
            _completions = Array.Empty<string>();
            UpdateHint();
        }
        else
            _view.Document.ReleaseFocus();
        return true;
    }

    private void SetInput(string text)
    {
        _input.SetValueWithoutNotify(text);
        _input.SelectRange(text.Length, text.Length);
        UpdateHint();
    }

    private void OnKey(KeyDownEvent evt)
    {
        if (_listener == null || _commands == null)
            return;
        switch (evt.keyCode)
        {
            case KeyCode.Return:
            case KeyCode.KeypadEnter:
                var text = _input.value.Trim();
                if (text.Length > 0)
                {
                    _listener.History.Add(text);
                    _listener.Buffer.Add("Command", "> " + text, command: true);
                    try
                    {
                        var result = _commands.Execute(text);
                        if (result.Length > 0)
                            _listener.Buffer.Add("Command", result, command: true);
                    }
                    catch (Exception error)
                    {
                        _listener.Buffer.Add("Command", error.Message, ConsoleSeverity.Error, command: true);
                    }
                }
                _completions = Array.Empty<string>();
                SetInput("");
                break;
            case KeyCode.UpArrow:
            case KeyCode.DownArrow:
                _completions = Array.Empty<string>();
                SetInput(_listener.History.Move(evt.keyCode == KeyCode.UpArrow ? -1 : 1, _input.value));
                break;
            case KeyCode.Tab:
                if (_completions.Length == 0)
                {
                    _completions = _commands.Complete(_input.value);
                    _completionIndex = -1;
                }
                if (_completions.Length > 0)
                {
                    _completionIndex =
                        (_completionIndex + (evt.shiftKey ? _completions.Length - 1 : 1) + _completions.Length) % _completions.Length;
                    SetInput(_completions[_completionIndex]);
                }
                break;
            default:
                return;
        }
        evt.StopImmediatePropagation();
        evt.PreventDefault();
    }

    internal void Tick()
    {
        // IsOpen also includes inactive dock tabs; resolved display excludes those tabs.
        if (
            _listener == null
            || !_view.Document.Visible
            || !_view.Windows.IsOpen("Console")
            || _view.Element("Console").resolvedStyle.display == DisplayStyle.None
        )
            return;
        ApplyTextSize();
        if (Time.unscaledTime < _nextRefresh)
            return;
        _nextRefresh = Time.unscaledTime + .1f;
        var version = _listener.Buffer.Version;
        if (_version == version)
            return;
        var entries = _listener.Buffer.Snapshot();
        _version = version;
        _visible.Clear();
        foreach (var entry in entries)
            if (EditorConsoleBuffer.Matches(entry, _all.value, _info.value, _warning.value, _error.value, _debug.value, _search.value))
                _visible.Add(entry);
        var selectedIndex = _selected == null ? -1 : _visible.IndexOf(_selected);
        _list.RefreshItems();
        _list.SetSelectionWithoutNotify(selectedIndex < 0 ? Array.Empty<int>() : new[] { selectedIndex });
        _selected = selectedIndex < 0 ? null : _visible[selectedIndex];
        _details.SetValueWithoutNotify(_selected?.Text ?? "Select a message to see its full text.");
        _counts.text = $"{_visible.Count} shown / {entries.Length} retained · {_listener.Buffer.Discarded} older messages discarded";
        if (_scroll.value && _visible.Count > 0)
            FollowOutput();
    }
}
