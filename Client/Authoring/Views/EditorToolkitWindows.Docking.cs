using UnityEngine;
using UnityEngine.UIElements;
using WTT.Campaigns.UI.Controls;
using ZLinq;

namespace WTT.Campaigns.Client.Authoring.Views;

internal sealed partial class EditorToolkitWindows
{
    private bool _chromeDirty;

    private void RebuildChrome()
    {
        if (Interacting)
        {
            _chromeDirty = true;
            return;
        }
        _chromeDirty = false;
        _chrome.Clear();
        _bars.Clear();
        _dividers.Clear();
        var open = OpenIds();
        _dock = EditorDockLayout.FitDisplay(_dock, Area, open);
        foreach (var node in EditorDockLayout.Nodes(_dock))
        {
            if (!EditorDockLayout.Visible(node, open))
                continue;
            if (node.Kind == "tabs")
            {
                var bar = _view.Document.Clone<ScrollView>("DockTabs");
                _bars.Add(node.Id, bar);
                _chrome.Add(bar);
                foreach (var id in node.Tabs.AsValueEnumerable().Where(open.Contains))
                {
                    var tab = _view.Document.Clone<Label>("DockTab");
                    tab.text =
                        id == "Tool:Routes" && _view.ContentMode == WTT.Campaigns.Shared.Authoring.EditorContentMode.Level ? "Extracts"
                        : id.StartsWith("Tool:") ? RaidEditorView.ToolTitle(id.Substring(5))
                        : id == "LootConfiguration" ? "Loot configuration"
                        : id == "Terrain" ? "Terrain"
                        : id == "Navigation" ? "Navigation"
                        : id == "Console" ? "Console"
                        : id == "Inspector" ? "Properties"
                        : id == "Controls" ? "Help"
                        : "Environment";
                    tab.EnableInClassList("editor-active-tab", id == node.Active);
                    tab.userData = id;
                    tab.tooltip = "Drag to reorder, dock or float";
                    bar.Add(tab);
                    BindDrag(id, tab, false, true);
                }
            }
            else if (
                node.First != null
                && node.Second != null
                && EditorDockLayout.Visible(node.First, open)
                && EditorDockLayout.Visible(node.Second, open)
            )
            {
                var divider = _view.Document.Clone<VisualElement>("DockDivider");
                _dividers.Add(node.Id, divider);
                _chrome.Add(divider);
                divider.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button != 0 || Modal || Interacting)
                        return;
                    _dragLayout = Clone(CaptureLayout());
                    _dragHandle = divider;
                    _pointerId = evt.pointerId;
                    _dragId = "split:" + node.Id;
                    divider.CapturePointer(evt.pointerId);
                    evt.StopPropagation();
                });
                divider.RegisterCallback<PointerMoveEvent>(evt =>
                {
                    if (_dragHandle != divider || !_dockRects.TryGetValue(node.Id, out var r))
                        return;
                    var horizontal = node.Kind == "horizontal";
                    var size = Math.Max(1, (horizontal ? r.Width : r.Height) - 6);
                    var point = _view.Element("Workspace").WorldToLocal(evt.position);
                    var a = EditorDockLayout.Minimum(node.First!, OpenIds());
                    var b = EditorDockLayout.Minimum(node.Second!, OpenIds());
                    var min = (horizontal ? a.Width : a.Height) / size;
                    var max = 1 - (horizontal ? b.Width : b.Height) / size;
                    if (min <= max)
                        node.Ratio = Math.Clamp(
                            (horizontal ? point.x - r.X : point.y - r.Y) / size,
                            Math.Max(.001f, min),
                            Math.Min(.999f, max)
                        );
                    FitPanels();
                    evt.StopPropagation();
                });
                divider.RegisterCallback<PointerUpEvent>(evt =>
                {
                    if (_dragHandle == divider)
                    {
                        FinishInteraction();
                        evt.StopPropagation();
                    }
                });
                divider.RegisterCallback<PointerCaptureOutEvent>(_ =>
                {
                    if (_dragHandle == divider)
                        CancelInteraction();
                });
            }
        }
    }

    private void BindDrag(string id, VisualElement handle, bool resize, bool tab = false)
    {
        handle.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (
                evt.button != 0
                || Modal
                || Interacting
                || evt.target is Button
                || (evt.target is VisualElement child && child.GetFirstAncestorOfType<Button>() != null)
            )
                return;
            _dragLayout = Clone(CaptureLayout());
            _dragId = id;
            _resize = resize;
            _dragMoved = false;
            _candidate = null;
            _before = _windowBounds.GetValueOrDefault(id);
            _start = evt.position;
            _dragHandle = handle;
            _pointerId = evt.pointerId;
            handle.CapturePointer(evt.pointerId);
            var group = EditorDockLayout.Nodes(_dock).AsValueEnumerable().FirstOrDefault(n => n.Tabs.AsValueEnumerable().Contains(id));
            if (group != null)
                group.Active = id;
            if (id.StartsWith("Tool:"))
                _view.Activate(id.Substring(5));
            FitPanels();
            Focus(id);
            evt.StopPropagation();
        });
        handle.RegisterCallback<PointerMoveEvent>(evt =>
        {
            if (_dragHandle != handle || _dragLayout == null)
                return;
            var delta = (Vector2)evt.position - _start;
            if (!_dragMoved && delta.sqrMagnitude < 25)
                return;
            if (!_dragMoved && !_resize)
            {
                _dock = EditorDockLayout.Remove(_dock, id) ?? new() { Kind = "viewport" };
                if (tab)
                    _before.y -= EditorDockLayout.TabHeight;
            }
            _dragMoved = true;
            var rect = _resize
                ? new Rect(
                    _before.x,
                    _before.y,
                    Math.Max(280, _before.width + delta.x),
                    Math.Max(id == "Console" ? 300 : 180, _before.height + delta.y)
                )
                : new Rect(_before.x + delta.x, _before.y + delta.y, _before.width, _before.height);
            var p = _panels[id];
            p.Width = rect.width;
            p.Height = rect.height;
            p.X = rect.center.x / _view.Document.Width - .5f;
            p.Y = .5f - rect.center.y / _view.Document.Height;
            p.Visible = true;
            if (_resize)
                p.ManualSize = true;
            _sized.Add(id);
            FitPanels();
            Focus(id);
            if (!_resize)
                PreviewDock(id, _view.Element("Workspace").WorldToLocal(evt.position));
            evt.StopPropagation();
        });
        handle.RegisterCallback<PointerUpEvent>(evt =>
        {
            if (_dragHandle != handle)
                return;
            if (_dragMoved && _candidate != null && !_resize)
                _dock = _candidate;
            FinishInteraction();
            evt.StopPropagation();
        });
        handle.RegisterCallback<PointerCaptureOutEvent>(_ =>
        {
            if (_dragHandle == handle)
                CancelInteraction();
        });
    }

    private void PreviewDock(string id, Vector2 point)
    {
        _candidate = null;
        _dropPreview.style.display = DisplayStyle.None;
        var area = Area;
        if (!area.Contains(point.x, point.y))
            return;
        // A floating window under the pointer owns the drop, even above a dock.
        var floating = _panels
            .Keys.AsValueEnumerable()
            .Reverse()
            .FirstOrDefault(k => k != id && IsOpen(k) && !Docked(k) && _windowBounds[k].Contains(point));
        var root = Clone(_dock);
        EditorDockNode? target = null;
        EditorDockRect r = area;
        if (floating != null)
        {
            // Both floating windows form a dock group at the nearest workspace edge.
            var side = _windowBounds[floating].center.x < _view.Document.Width / 2 ? "left" : "right";
            root = EditorDockLayout.Dock(root, floating, root.Id, side);
            target = EditorDockLayout.Nodes(root).AsValueEnumerable().First(n => n.Tabs.AsValueEnumerable().Contains(floating));
            var rectangles = EditorDockLayout.Arrange(root, area, OpenIds());
            r = rectangles[target.Id];
        }
        string edge = "";
        if (floating == null)
        {
            edge =
                point.x < area.X + 22 ? "left"
                : point.x > area.X + area.Width - 22 ? "right"
                : point.y < area.Y + 18 ? "top"
                : point.y > area.Y + area.Height - 18 ? "bottom"
                : "";
            target =
                edge.Length > 0
                    ? root
                    : EditorDockLayout
                        .Nodes(root)
                        .AsValueEnumerable()
                        .LastOrDefault(n =>
                            n.Kind is "tabs" or "viewport"
                            && _dockRects.TryGetValue(n.Id, out var bounds)
                            && bounds.Contains(point.x, point.y)
                        );
            if (target == null)
                return;
            r = edge.Length > 0 ? area : _dockRects[target.Id];
        }
        var sideName =
            floating != null ? "center"
            : edge.Length > 0 ? edge
            : point.y < r.Y + EditorDockLayout.TabHeight && target!.Kind == "tabs" ? "center"
            : point.x < r.X + r.Width * .22f ? "left"
            : point.x > r.X + r.Width * .78f ? "right"
            : point.y < r.Y + r.Height * .22f ? "top"
            : point.y > r.Y + r.Height * .78f ? "bottom"
            : "center";
        if (sideName == "center" && target!.Kind != "tabs")
            return;
        var tabIndex = -1;
        if (sideName == "center" && _bars.TryGetValue(target!.Id, out var bar) && point.y < r.Y + EditorDockLayout.TabHeight)
        {
            tabIndex = target.Tabs.Length;
            foreach (var label in bar.Query<Label>().ToList())
            {
                if (label.userData is not string other || other == id)
                    continue;
                if (_view.Element("Workspace").WorldToLocal(label.worldBound.center).x >= point.x)
                {
                    tabIndex = Array.IndexOf(target.Tabs, other);
                    break;
                }
            }
        }
        root = EditorDockLayout.Dock(root, id, target!.Id, sideName, tabIndex);
        var minimum = EditorDockLayout.Minimum(root, OpenIds());
        if (
            minimum.Width > area.Width
            || minimum.Height > area.Height
            || !EditorDockLayout.Valid(root, _panels.Keys.AsValueEnumerable().ToHashSet())
        )
            return;
        var group = EditorDockLayout.Nodes(root).AsValueEnumerable().FirstOrDefault(n => n.Tabs.AsValueEnumerable().Contains(id));
        if (group == null)
            return;
        var preview = EditorDockLayout.Arrange(root, area, OpenIds())[group.Id];
        _candidate = root;
        Place(_dropPreview, new(preview.X, preview.Y, preview.Width, preview.Height));
        _dropPreview.style.display = DisplayStyle.Flex;
        _dropPreview.BringToFront();
    }

    private void FinishInteraction()
    {
        var handle = _dragHandle;
        _dragHandle = null;
        _dragLayout = null;
        _candidate = null;
        if (handle != null && handle.HasPointerCapture(_pointerId))
            handle.ReleasePointer(_pointerId);
        _dropPreview.style.display = DisplayStyle.None;
        RebuildChrome();
        FitPanels();
        LayoutChanged?.Invoke();
    }

    internal void CancelInteraction()
    {
        var handle = _dragHandle;
        var snapshot = _dragLayout;
        _dragHandle = null;
        _dragLayout = null;
        _candidate = null;
        if (snapshot != null)
        {
            _dock = snapshot.Dock!;
            foreach (var placement in snapshot.Windows)
                _panels[placement.Id] = placement;
        }
        if (handle != null && handle.HasPointerCapture(_pointerId))
            handle.ReleasePointer(_pointerId);
        _dropPreview.style.display = DisplayStyle.None;
        if (snapshot != null || _chromeDirty)
        {
            RebuildChrome();
            FitPanels();
        }
    }
}
