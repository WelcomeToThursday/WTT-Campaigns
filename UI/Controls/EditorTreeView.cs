using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace WTT.Campaigns.UI.Controls;

// Pooled uGUI renderer for ordered editor trees. Domain adapters supply an
// EditorTreeModel; this class owns foldouts, scrolling and row presentation.
public sealed class EditorTreeView : IDisposable
{
    private const float RowHeight = 30f;
    private const float TopPadding = 4f;
    private const float BottomPadding = 12f;
    private const int MinimumPool = 24;
    private const int MaximumPool = 96;

    private readonly ScrollRect _scroll;
    private readonly RectTransform _content;
    private readonly RectTransform _viewport;
    private readonly GameObject _template;
    private readonly VerticalLayoutGroup? _normalLayout;
    private readonly ContentSizeFitter? _normalFitter;
    private readonly List<GameObject> _normalRows = new();
    private readonly List<bool> _normalRowStates = new();
    private readonly List<PooledRow> _pool = new();
    private readonly Dictionary<string, HashSet<string>> _expandedByContext = new(StringComparer.Ordinal);
    private readonly UnityAction<Vector2> _scrollChanged;
    private readonly Vector2 _normalSizeDelta;
    private readonly Vector2 _normalAnchoredPosition;
    private readonly bool _normalLayoutEnabled;
    private readonly bool _normalFitterEnabled;
    private bool _active;
    private bool _disposed;
    private bool _dirty = true;
    private bool _renderPending;
    private string _contextId = "";
    private string _search = "";
    private string _selection = "";
    private long _revision = long.MinValue;
    private EditorTreeModel? _model;
    private Action<string>? _select;
    private Func<ISet<string>, EditorTreeModel>? _builder;
    private Action<string, string>? _showTooltip;
    private Action? _hideTooltip;
    private readonly RenderTicker _ticker;

    public EditorTreeView(ScrollRect scroll)
    {
        _scroll = scroll ?? throw new ArgumentNullException(nameof(scroll));
        _content = scroll.content ?? throw new InvalidOperationException("The tree scroll has no content transform.");
        _viewport = scroll.viewport ?? throw new InvalidOperationException("The tree scroll has no viewport transform.");
        _normalLayout = _content.GetComponent<VerticalLayoutGroup>();
        _normalFitter = _content.GetComponent<ContentSizeFitter>();
        _normalLayoutEnabled = _normalLayout?.enabled == true;
        _normalFitterEnabled = _normalFitter?.enabled == true;
        _normalSizeDelta = _content.sizeDelta;
        _normalAnchoredPosition = _content.anchoredPosition;

        for (var i = 0; i < 10; i++)
        {
            var row = _content.Find("Row" + i);
            if (row == null)
                continue;
            _normalRows.Add(row.gameObject);
            _normalRowStates.Add(row.gameObject.activeSelf);
        }

        _template =
            _content.Find("Row0")?.gameObject ?? throw new InvalidOperationException("The library tree is missing its row template.");
        EnsurePool();

        _scrollChanged = _ => RenderVisible();
        _scroll.onValueChanged.AddListener(_scrollChanged);
        _ticker = _content.gameObject.AddComponent<RenderTicker>();
        _ticker.Tick = RenderPending;
    }

    public int RecordCount => _model?.SelectableCount ?? 0;
    public int VisibleCount => _model?.Visible.Count ?? 0;
    public bool Active => _active;

    public void BindSelection(Action<string> select) => _select = select;

    public void BindTooltips(Action<string, string> show, Action hide)
    {
        _showTooltip = show;
        _hideTooltip = hide;
        foreach (var row in _pool)
            BindTooltip(row);
    }

    public void Refresh(string contextId, long revision, string search, string selection, Func<ISet<string>, EditorTreeModel> build)
    {
        if (_disposed)
            return;
        if (!_active)
            SetActive(true);

        contextId ??= "";
        search ??= "";
        selection ??= "";
        var contextChanged = _contextId != contextId;
        var selectionChanged = contextChanged || _selection != selection;
        if (!_dirty && !contextChanged && _revision == revision && _search == search && _selection == selection)
            return;

        _contextId = contextId;
        _revision = revision;
        _search = search;
        _selection = selection;
        _builder = build ?? throw new ArgumentNullException(nameof(build));
        if (!_expandedByContext.TryGetValue(contextId, out var expanded))
        {
            expanded = new HashSet<string>(StringComparer.Ordinal);
            _expandedByContext.Add(contextId, expanded);
            var initial = build(expanded);
            foreach (var key in EditorTreeModel.DefaultExpanded(initial))
                expanded.Add(key);
        }

        var model = build(expanded);
        if (selectionChanged && EditorTreeModel.ExpandForSelection(model, selection, expanded))
            model = build(expanded);
        _model = model;
        _dirty = false;
        RenderLayout(selectionChanged || contextChanged);
    }

    public void SetActive(bool active)
    {
        if (_disposed || _active == active)
            return;
        _active = active;
        if (active)
        {
            for (var i = 0; i < _normalRows.Count; i++)
            {
                _normalRowStates[i] = _normalRows[i].activeSelf;
                _normalRows[i].SetActive(false);
            }
            if (_normalLayout != null)
                _normalLayout.enabled = false;
            if (_normalFitter != null)
                _normalFitter.enabled = false;
            foreach (var row in _pool)
                row.GameObject.SetActive(false);
            _dirty = true;
            return;
        }

        foreach (var row in _pool)
            row.GameObject.SetActive(false);
        _renderPending = false;
        for (var i = 0; i < _normalRows.Count; i++)
            _normalRows[i].SetActive(_normalRowStates[i]);
        if (_normalLayout != null)
            _normalLayout.enabled = _normalLayoutEnabled;
        if (_normalFitter != null)
            _normalFitter.enabled = _normalFitterEnabled;
        _content.sizeDelta = _normalSizeDelta;
        _content.anchoredPosition = _normalAnchoredPosition;
        _scroll.verticalNormalizedPosition = 1;
        _model = null;
        _builder = null;
        _dirty = true;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _scroll.onValueChanged.RemoveListener(_scrollChanged);
        foreach (var row in _pool)
            if (row.GameObject)
                UnityEngine.Object.Destroy(row.GameObject);
        if (_ticker)
            UnityEngine.Object.Destroy(_ticker);
        _pool.Clear();
    }

    private void Toggle(string key)
    {
        if (!_active || _builder == null || !_expandedByContext.ContainsKey(_contextId))
            return;
        var expanded = _expandedByContext[_contextId];
        if (!expanded.Remove(key))
            expanded.Add(key);
        _dirty = true;
        Refresh(_contextId, _revision, _search, _selection, _builder);
    }

    private void EnsurePool()
    {
        var viewportHeight = Mathf.Max(1, _viewport.rect.height);
        var count = Mathf.Clamp(Mathf.CeilToInt(viewportHeight / RowHeight) + 8, MinimumPool, MaximumPool);
        for (var i = _pool.Count; i < count; i++)
        {
            var instance = UnityEngine.Object.Instantiate(_template, _content, false);
            instance.name = "EditorTreeRow" + i;
            var button = instance.GetComponent<Button>() ?? throw new InvalidOperationException("The library row has no button.");
            var selection = instance.GetComponent<EditorRowSelection>() ?? instance.AddComponent<EditorRowSelection>();
            var label = instance.GetComponentInChildren<Text>(true) ?? throw new InvalidOperationException("The library row has no label.");
            foreach (var icon in instance.GetComponentsInChildren<RawImage>(true))
                icon.gameObject.SetActive(false);
            foreach (var text in instance.GetComponentsInChildren<Text>(true))
                if (text != label && text.name.StartsWith("SceneIconStatus", StringComparison.Ordinal))
                    text.gameObject.SetActive(false);
            var disclosure = CreateDisclosure(instance.transform, label.font);
            _pool.Add(new PooledRow(instance, button, selection, label, disclosure));
            BindTooltip(_pool[^1]);
            instance.SetActive(false);
        }
    }

    private static Disclosure CreateDisclosure(Transform parent, Font? font)
    {
        var objectRoot = new GameObject("Disclosure", typeof(RectTransform), typeof(Image), typeof(Button));
        objectRoot.transform.SetParent(parent, false);
        var rect = (RectTransform)objectRoot.transform;
        rect.anchorMin = new Vector2(0, .5f);
        rect.anchorMax = new Vector2(0, .5f);
        rect.pivot = new Vector2(0, .5f);
        rect.sizeDelta = new Vector2(26, 28);
        rect.anchoredPosition = Vector2.zero;
        var image = objectRoot.GetComponent<Image>();
        image.color = Color.clear;
        var button = objectRoot.GetComponent<Button>();
        button.targetGraphic = image;
        var textObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(objectRoot.transform, false);
        var text = textObject.GetComponent<Text>();
        text.font = font;
        text.fontSize = 15;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = EditorTarkovTheme.Ink;
        text.raycastTarget = false;
        var textRect = (RectTransform)textObject.transform;
        UiElements.Stretch(textRect, 0, 0, 0, 0);
        var selection = objectRoot.AddComponent<EditorRowSelection>();
        return new Disclosure(button, selection, text);
    }

    private void RenderLayout(bool revealSelection)
    {
        if (_model == null)
            return;
        var totalHeight = TopPadding + _model.Visible.Count * RowHeight + BottomPadding;
        _content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, totalHeight);
        if (revealSelection && _selection.Length > 0)
            RevealSelection();
        RenderVisible();
    }

    private void RevealSelection()
    {
        if (_model == null)
            return;
        var index = -1;
        for (var i = 0; i < _model.Visible.Count; i++)
            if (_model.Visible[i].Id == _selection)
            {
                index = i;
                break;
            }
        if (index < 0)
            return;

        var viewportHeight = Mathf.Max(1, _viewport.rect.height);
        var top = TopPadding + index * RowHeight;
        var bottom = top + RowHeight;
        var scrollY = Mathf.Max(0, _content.anchoredPosition.y);
        if (top < scrollY)
            scrollY = top;
        else if (bottom > scrollY + viewportHeight)
            scrollY = bottom - viewportHeight;
        var maxScroll = Mathf.Max(0, _content.rect.height - viewportHeight);
        _content.anchoredPosition = new Vector2(_content.anchoredPosition.x, Mathf.Clamp(scrollY, 0, maxScroll));
    }

    private void RenderVisible()
    {
        if (!_active || _model == null)
            return;
        if (AnyPressed())
        {
            _renderPending = true;
            return;
        }
        _renderPending = false;
        EnsurePool();
        var viewportHeight = Mathf.Max(1, _viewport.rect.height);
        var maxScroll = Mathf.Max(0, _content.rect.height - viewportHeight);
        if (_content.anchoredPosition.y > maxScroll)
            _content.anchoredPosition = new Vector2(_content.anchoredPosition.x, maxScroll);
        var scrollY = Mathf.Max(0, _content.anchoredPosition.y);
        var start = Mathf.Max(0, Mathf.FloorToInt((scrollY - TopPadding) / RowHeight) - 2);
        for (var i = 0; i < _pool.Count; i++)
        {
            var absolute = start + i;
            var row = _pool[i];
            if (absolute >= _model.Visible.Count)
            {
                row.GameObject.SetActive(false);
                continue;
            }

            var node = _model.Visible[absolute];
            row.GameObject.SetActive(true);
            var rect = (RectTransform)row.GameObject.transform;
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(.5f, 1);
            rect.sizeDelta = new Vector2(0, RowHeight);
            rect.anchoredPosition = new Vector2(0, -(TopPadding + absolute * RowHeight));
            BindRow(row, node);
        }
    }

    private void RenderPending()
    {
        if (_renderPending && !AnyPressed())
            RenderVisible();
    }

    private void BindRow(PooledRow row, EditorTreeNode node)
    {
        var storedExpanded = _expandedByContext.TryGetValue(_contextId, out var keys) && keys.Contains(node.Key);
        var expanded = _search.Trim().Length > 0 || storedExpanded;
        row.Selection.Identity = node.Selectable ? node.Id : "";
        row.Button.onClick.RemoveAllListeners();
        if (node.Selectable)
        {
            row.Button.onClick.AddListener(() =>
            {
                var id = row.Selection.Consume();
                if (id.Length > 0)
                    _select?.Invoke(id);
            });
        }
        row.Button.interactable = node.Selectable;
        row.Button.targetGraphic.color =
            node.Selectable && node.Id == _selection ? EditorTarkovTheme.Selected : EditorTarkovTheme.Container;
        row.Label.text = node.Label;
        row.Label.color = node.Selectable ? EditorTarkovTheme.Ink : EditorTarkovTheme.Muted;
        row.Label.alignment = TextAnchor.MiddleLeft;
        row.Label.horizontalOverflow = HorizontalWrapMode.Overflow;
        row.Label.verticalOverflow = VerticalWrapMode.Truncate;
        row.Label.raycastTarget = false;
        var labelRect = row.Label.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(32 + node.Depth * 14, 1);
        labelRect.offsetMax = new Vector2(-4, -1);

        var disclosureRect = (RectTransform)row.Disclosure.Button.transform;
        disclosureRect.anchoredPosition = new Vector2(node.Depth * 14, 0);
        row.Disclosure.Selection.Identity = node.HasChildren ? node.Key : "";
        row.Disclosure.Button.onClick.RemoveAllListeners();
        row.Disclosure.Button.interactable = node.HasChildren;
        row.Disclosure.Button.gameObject.SetActive(node.HasChildren);
        row.Disclosure.Button.targetGraphic.color = Color.clear;
        row.Disclosure.Label.text = expanded ? "v" : ">";
        row.Disclosure.Label.color = node.HasChildren
            ? expanded
                ? EditorTarkovTheme.Ink
                : EditorTarkovTheme.Muted
            : Color.clear;
        if (node.HasChildren)
        {
            row.Disclosure.Button.onClick.AddListener(() => Toggle(row.Disclosure.Selection.Consume()));
        }
        row.Tooltip = node.Path + (node.HasChildren ? "\nClick the arrow to " + (expanded ? "collapse" : "expand") : "");
        row.Disclosure.Tooltip = node.Path + (node.HasChildren ? "\nClick to " + (expanded ? "collapse" : "expand") : "");
    }

    private bool AnyPressed()
    {
        foreach (var row in _pool)
            if (row.Selection.Pressed || row.Disclosure.Selection.Pressed)
                return true;
        return false;
    }

    private void BindTooltip(PooledRow row)
    {
        var rowTooltip = row.GameObject.GetComponent<EditorControlTooltip>() ?? row.GameObject.AddComponent<EditorControlTooltip>();
        rowTooltip.Enter = () => _showTooltip?.Invoke(row.GameObject.name, row.Tooltip);
        rowTooltip.Exit = () => _hideTooltip?.Invoke();
        var disclosureTooltip =
            row.Disclosure.Button.gameObject.GetComponent<EditorControlTooltip>()
            ?? row.Disclosure.Button.gameObject.AddComponent<EditorControlTooltip>();
        disclosureTooltip.Enter = () => _showTooltip?.Invoke(row.Disclosure.Button.name, row.Disclosure.Tooltip);
        disclosureTooltip.Exit = () => _hideTooltip?.Invoke();
    }

    private sealed class PooledRow
    {
        internal PooledRow(GameObject gameObject, Button button, EditorRowSelection selection, Text label, Disclosure disclosure)
        {
            GameObject = gameObject;
            Button = button;
            Selection = selection;
            Label = label;
            Disclosure = disclosure;
            Tooltip = "";
        }

        internal GameObject GameObject { get; }
        internal Button Button { get; }
        internal EditorRowSelection Selection { get; }
        internal Text Label { get; }
        internal Disclosure Disclosure { get; }
        internal string Tooltip { get; set; }
    }

    private sealed class Disclosure
    {
        internal Disclosure(Button button, EditorRowSelection selection, Text label)
        {
            Button = button;
            Selection = selection;
            Label = label;
            Tooltip = "";
        }

        internal Button Button { get; }
        internal EditorRowSelection Selection { get; }
        internal Text Label { get; }
        internal string Tooltip { get; set; }
    }

    private sealed class RenderTicker : MonoBehaviour
    {
        internal Action? Tick;

        private void LateUpdate() => Tick?.Invoke();
    }
}
