using System;
using System.Collections.Generic;
using System.Linq;

namespace WTT.Campaigns.UI.Controls;

// Pure layout operations, shared by the runtime docking host and offline checks.
[Serializable]
public sealed class EditorDockNode
{
    public string Id = Guid.NewGuid().ToString("N");
    public string Kind = "tabs";
    public string[] Tabs = Array.Empty<string>();
    public string Active = "";
    public float Ratio = .5f;
    public EditorDockNode? First,
        Second;

    public static EditorDockNode Group(params string[] tabs) => new() { Tabs = tabs, Active = tabs.FirstOrDefault() ?? "" };

    public static EditorDockNode Split(string kind, EditorDockNode first, EditorDockNode second, float ratio = .5f) =>
        new()
        {
            Kind = kind,
            First = first,
            Second = second,
            Ratio = ratio,
        };

    public static EditorDockNode Default() =>
        Split("horizontal", Group("Tool:Layouts"), Split("horizontal", new() { Kind = "viewport" }, Group("Inspector"), .72f), .24f);
}

public readonly struct EditorDockRect
{
    public readonly float X,
        Y,
        Width,
        Height;

    public EditorDockRect(float x, float y, float width, float height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public bool Contains(float x, float y) => x >= X && y >= Y && x <= X + Width && y <= Y + Height;
}

public static class EditorDockLayout
{
    public const float Divider = 6,
        TabHeight = 26,
        ToolWidth = 280,
        ToolHeight = 180,
        ViewWidth = 320;

    // A layout created on a larger display can exceed today's minimums. Group
    // its tools as tabs before allowing the viewport or fields to become unusable.
    public static EditorDockNode FitDisplay(EditorDockNode root, EditorDockRect area, ISet<string> visible)
    {
        var minimum = Minimum(root, visible);
        if (minimum.Width <= area.Width && minimum.Height <= area.Height)
            return root;
        var nodes = Nodes(root).ToArray();
        var viewport = nodes.First(n => n.Kind == "viewport");
        var tabs = GroupAll(nodes);
        var group = EditorDockNode.Group(tabs);
        group.Active = nodes.Where(n => n.Kind == "tabs").Select(n => n.Active).FirstOrDefault(visible.Contains) ?? group.Active;
        var toolHeight = Minimum(group, visible).Height;
        if (area.Width >= ToolWidth + Divider + ViewWidth && area.Height >= toolHeight)
            return EditorDockNode.Split("horizontal", group, viewport, .35f);
        if (area.Height >= toolHeight + Divider + 180 && area.Width >= ViewWidth)
            return EditorDockNode.Split("vertical", group, viewport, .45f);
        return viewport; // Visible windows become clamped floating windows.
    }

    private static string[] GroupAll(EditorDockNode[] nodes) => nodes.SelectMany(n => n.Tabs).Distinct().ToArray();

    public static IEnumerable<EditorDockNode> Nodes(EditorDockNode? node)
    {
        if (node == null)
            yield break;
        yield return node;
        foreach (var child in Nodes(node.First))
            yield return child;
        foreach (var child in Nodes(node.Second))
            yield return child;
    }

    public static bool Visible(EditorDockNode node, ISet<string> visible) =>
        node.Kind == "viewport"
        || node.Tabs.Any(visible.Contains)
        || node.First != null && Visible(node.First, visible)
        || node.Second != null && Visible(node.Second, visible);

    public static (float Width, float Height) Minimum(EditorDockNode node, ISet<string> visible)
    {
        if (!Visible(node, visible))
            return (0, 0);
        if (node.Kind == "viewport")
            return (ViewWidth, 180);
        if (node.Kind == "tabs")
            return (ToolWidth, (node.Tabs.Contains("Console") && visible.Contains("Console") ? 300 : ToolHeight) + TabHeight);
        var a = Minimum(node.First!, visible);
        var b = Minimum(node.Second!, visible);
        if (a.Width == 0)
            return b;
        if (b.Width == 0)
            return a;
        return node.Kind == "horizontal"
            ? (a.Width + b.Width + Divider, Math.Max(a.Height, b.Height))
            : (Math.Max(a.Width, b.Width), a.Height + b.Height + Divider);
    }

    public static Dictionary<string, EditorDockRect> Arrange(EditorDockNode root, EditorDockRect area, ISet<string> visible)
    {
        var result = new Dictionary<string, EditorDockRect>();
        void Walk(EditorDockNode node, EditorDockRect rect)
        {
            if (!Visible(node, visible))
                return;
            result[node.Id] = rect;
            if (node.First == null || node.Second == null)
                return;
            if (!Visible(node.First, visible))
            {
                Walk(node.Second, rect);
                return;
            }
            if (!Visible(node.Second, visible))
            {
                Walk(node.First, rect);
                return;
            }
            var horizontal = node.Kind == "horizontal";
            var available = Math.Max(0, (horizontal ? rect.Width : rect.Height) - Divider);
            var a = Minimum(node.First, visible);
            var b = Minimum(node.Second, visible);
            var minA = horizontal ? a.Width : a.Height;
            var minB = horizontal ? b.Width : b.Height;
            var split =
                available >= minA + minB
                    ? Math.Max(minA, Math.Min(available - minB, available * node.Ratio))
                    : available * minA / Math.Max(1, minA + minB);
            Walk(node.First, horizontal ? new(rect.X, rect.Y, split, rect.Height) : new(rect.X, rect.Y, rect.Width, split));
            Walk(
                node.Second,
                horizontal
                    ? new(rect.X + split + Divider, rect.Y, available - split, rect.Height)
                    : new(rect.X, rect.Y + split + Divider, rect.Width, available - split)
            );
        }
        Walk(root, area);
        return result;
    }

    public static EditorDockNode? Remove(EditorDockNode? node, string window)
    {
        if (node == null)
            return null;
        node.Tabs = node.Tabs.Where(t => t != window).ToArray();
        if (node.Kind == "tabs")
        {
            if (!node.Tabs.Contains(node.Active))
                node.Active = node.Tabs.FirstOrDefault() ?? "";
            return node.Tabs.Length == 0 ? null : node;
        }
        if (node.Kind == "viewport")
            return node;
        node.First = Remove(node.First, window);
        node.Second = Remove(node.Second, window);
        return node.First == null ? node.Second
            : node.Second == null ? node.First
            : node;
    }

    // Call on a cloned candidate, then accept only if Minimum fits the workspace.
    public static EditorDockNode Dock(EditorDockNode root, string window, string target, string side, int tabIndex = -1)
    {
        var targetNode = Nodes(root).FirstOrDefault(n => n.Id == target);
        if (targetNode == null)
            return root;
        if (side == "center" && targetNode.Kind != "tabs")
            return root;
        if (side == "center")
        {
            var tabs = targetNode.Tabs.Where(t => t != window).ToList();
            tabs.Insert(tabIndex < 0 ? tabs.Count : Math.Min(tabIndex, tabs.Count), window);
            // Remove from other groups without collapsing this target.
            foreach (var node in Nodes(root).Where(n => n != targetNode))
                node.Tabs = node.Tabs.Where(t => t != window).ToArray();
            targetNode.Tabs = tabs.ToArray();
            targetNode.Active = window;
            // Moving the last tab out of a split must not leave empty branches
            // accumulating each time users rearrange their workspace.
            return Remove(root, "")!;
        }
        if (targetNode.Kind == "tabs" && targetNode.Tabs.Length == 1 && targetNode.Tabs[0] == window)
            return root;
        root = Remove(root, window) ?? new() { Kind = "viewport" };
        targetNode = Nodes(root).FirstOrDefault(n => n.Id == target) ?? root;
        var original = new EditorDockNode
        {
            Kind = targetNode.Kind,
            Tabs = targetNode.Tabs,
            Active = targetNode.Active,
            Ratio = targetNode.Ratio,
            First = targetNode.First,
            Second = targetNode.Second,
        };
        targetNode.Kind = side is "left" or "right" ? "horizontal" : "vertical";
        targetNode.Tabs = Array.Empty<string>();
        targetNode.Active = "";
        var before = side is "left" or "top";
        targetNode.First = before ? EditorDockNode.Group(window) : original;
        targetNode.Second = before ? original : EditorDockNode.Group(window);
        targetNode.Ratio = .5f;
        return root;
    }

    public static bool Valid(EditorDockNode root, ISet<string> known)
    {
        var ids = new HashSet<string>();
        var windows = new HashSet<string>();
        int viewport = 0;
        bool Walk(EditorDockNode? n, int depth)
        {
            if (
                n == null
                || depth > 32
                || string.IsNullOrEmpty(n.Id)
                || !ids.Add(n.Id)
                || !float.IsFinite(n.Ratio)
                || n.Ratio <= 0
                || n.Ratio >= 1
                || n.Tabs == null
            )
                return false;
            if (n.Kind == "viewport")
            {
                viewport++;
                return n.First == null && n.Second == null && n.Tabs.Length == 0;
            }
            if (n.Kind == "tabs")
                return n.First == null && n.Second == null && n.Tabs.All(t => known.Contains(t) && windows.Add(t));
            return n.Kind is "horizontal" or "vertical" && n.Tabs.Length == 0 && Walk(n.First, depth + 1) && Walk(n.Second, depth + 1);
        }
        return Walk(root, 0) && viewport == 1;
    }
}
