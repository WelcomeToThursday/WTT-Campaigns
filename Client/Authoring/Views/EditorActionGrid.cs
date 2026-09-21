using System.Runtime.CompilerServices;
using UnityEngine.UIElements;

namespace WTT.Campaigns.Client.Authoring.Views;

// USS supplies presentation; runtime geometry fits labels to the available panel width.
internal static class EditorActionGrid
{
    private static readonly ConditionalWeakTable<VisualElement, Grid> Grids = new();
    private static readonly CustomStyleProperty<float> Gap = new("--editor-action-gap");
    private static readonly CustomStyleProperty<float> MinimumWidth = new("--editor-action-min-width");

    internal static void Bind(VisualElement root)
    {
        if (root.ClassListContains("editor-action-grid"))
            Grids.GetValue(root, element => new Grid(element)).Queue();
        foreach (var grid in root.Query<VisualElement>(className: "editor-action-grid").ToList())
            if (grid != root)
                Grids.GetValue(grid, element => new Grid(element)).Queue();
    }

    internal static void Refresh(VisualElement? grid)
    {
        if (grid != null && Grids.TryGetValue(grid, out var state))
            state.Queue();
    }

    internal static void Remove(VisualElement grid)
    {
        grid.RemoveFromClassList("editor-action-grid");
        if (Grids.TryGetValue(grid, out var state))
            state.Clear();
    }

    private sealed class Grid
    {
        private readonly VisualElement _root;
        private readonly List<Button> _buttons = new();
        private VisualElement? _parent;
        private bool _queued;

        internal Grid(VisualElement root)
        {
            _root = root;
            root.RegisterCallback<GeometryChangedEvent>(Geometry);
            root.RegisterCallback<CustomStyleResolvedEvent>(_ => Queue());
            root.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                WatchParent(root.parent);
                Queue();
            });
            root.RegisterCallback<DetachFromPanelEvent>(_ => WatchParent(null));
        }

        private void WatchParent(VisualElement? parent)
        {
            _parent?.UnregisterCallback<GeometryChangedEvent>(Geometry);
            _parent = parent;
            _parent?.RegisterCallback<GeometryChangedEvent>(Geometry);
        }

        private void Geometry(GeometryChangedEvent _) => Queue();

        internal void Queue()
        {
            if (_queued || !_root.ClassListContains("editor-action-grid"))
                return;
            _queued = true;
            _root.schedule.Execute(() =>
            {
                _queued = false;
                Fit();
            });
        }

        internal void Clear()
        {
            _root.style.width = StyleKeyword.Null;
            _root.style.maxWidth = StyleKeyword.Null;
            foreach (var button in _root.Children())
                Reset(button);
        }

        private static void Reset(VisualElement button)
        {
            if (!button.ClassListContains("editor-grid-command"))
                return;
            button.RemoveFromClassList("editor-grid-command");
            button.style.width = button.style.maxWidth = button.style.flexBasis = StyleKeyword.Null;
            button.style.height = button.style.maxHeight = StyleKeyword.Null;
            button.style.marginRight = StyleKeyword.Null;
        }

        private void Fit()
        {
            if (!_root.ClassListContains("editor-action-grid") || _root.parent == null || _root.panel == null)
                return;
            // Scroll content may be intrinsically sized; the viewport still bounds its rows.
            var width = _root.parent.contentRect.width;
            for (var parent = _root.parent; parent != null; parent = parent.parent)
                if (parent is ScrollView scroll)
                {
                    width = Math.Min(width, scroll.contentViewport.contentRect.width);
                    break;
                }
            var style = _root.resolvedStyle;
            width -= style.marginLeft + style.marginRight;
            if (!float.IsFinite(width) || width <= 0)
                return;
            _root.style.width = width;
            _root.style.maxWidth = width;
            width -= style.paddingLeft + style.paddingRight + style.borderLeftWidth + style.borderRightWidth;
            if (width <= 0)
                return;
            var gap = _root.customStyle.TryGetValue(Gap, out var spacing) ? spacing : 4f;
            var minimum = _root.customStyle.TryGetValue(MinimumWidth, out var cellMinimum) ? cellMinimum : 88f;
            _buttons.Clear();
            foreach (var child in _root.Children())
            {
                if (child.resolvedStyle.display == DisplayStyle.None)
                    continue;
                if (child is Button button && !child.ClassListContains("editor-choice") && !child.ClassListContains("editor-choice-field"))
                    _buttons.Add(button);
                else
                {
                    LayoutButtons(width, minimum, gap);
                    Reset(child);
                }
            }
            LayoutButtons(width, minimum, gap);
        }

        private void LayoutButtons(float width, float minimum, float gap)
        {
            if (_buttons.Count == 0)
                return;
            var preferred = minimum;
            foreach (var button in _buttons)
            {
                var text = button.MeasureTextSize(button.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined);
                preferred = Math.Max(preferred, text.x + HorizontalInset(button) + 4);
            }
            var columns = EditorActionGridLayout.Columns(_buttons.Count, width, preferred, gap);
            var cellWidth = EditorActionGridLayout.CellWidth(width, columns, gap);
            var height = 26f;
            foreach (var button in _buttons)
            {
                var text = button.MeasureTextSize(button.text, Math.Max(1, cellWidth - HorizontalInset(button) - 4),
                    VisualElement.MeasureMode.Exactly, 0, VisualElement.MeasureMode.Undefined);
                var style = button.resolvedStyle;
                height = Math.Max(height, text.y + style.paddingTop + style.paddingBottom + style.borderTopWidth + style.borderBottomWidth + 4);
            }
            height = (float)Math.Ceiling(height);
            for (var index = 0; index < _buttons.Count; index++)
            {
                var button = _buttons[index];
                button.AddToClassList("editor-grid-command");
                button.style.width = button.style.maxWidth = button.style.flexBasis = cellWidth;
                button.style.height = height;
                button.style.maxHeight = StyleKeyword.None;
                button.style.marginRight = (index + 1) % columns == 0 ? 0 : gap;
            }
            _buttons.Clear();
        }

        private static float HorizontalInset(Button button)
        {
            var style = button.resolvedStyle;
            return style.paddingLeft + style.paddingRight + style.borderLeftWidth + style.borderRightWidth;
        }
    }
}
