using System;
using System.Collections.Generic;

namespace WTT.Campaigns.UI.Controls;

// UI-independent tree data shared by editor browsers. Adapters own their
// domain records; the renderer only needs ordered children and stable keys.
public sealed class EditorTreeNode
{
    public EditorTreeNode(
        string key,
        string label,
        string id = "",
        bool selectable = false,
        int depth = 0,
        string? path = null
    )
    {
        Key = key;
        Label = label;
        Id = id;
        Selectable = selectable;
        Depth = depth;
        Path = path ?? label;
    }

    public string Key { get; }
    public string Id { get; }
    public string Label { get; }
    public string Path { get; }
    public int Depth { get; }
    public bool Selectable { get; }
    public List<EditorTreeNode> Children { get; } = new();
    public bool HasChildren => Children.Count > 0;

    public bool Matches(string search) =>
        search.Length == 0 || Path.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
        || Id.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
}

public sealed class EditorTreeModel
{
    private EditorTreeModel(
        string contextId,
        IReadOnlyList<EditorTreeNode> roots,
        IReadOnlyList<EditorTreeNode> visible,
        int selectableCount
    )
    {
        ContextId = contextId;
        Roots = roots;
        Visible = visible;
        SelectableCount = selectableCount;
    }

    public string ContextId { get; }
    public IReadOnlyList<EditorTreeNode> Roots { get; }
    public IReadOnlyList<EditorTreeNode> Visible { get; }
    public int SelectableCount { get; }

    public static EditorTreeModel Create(
        string contextId,
        IReadOnlyList<EditorTreeNode> roots,
        string search,
        ISet<string>? expanded = null
    )
    {
        var visible = new List<EditorTreeNode>();
        var normalizedSearch = search.Trim();
        foreach (var root in roots)
            AppendVisible(root, normalizedSearch, expanded, visible);
        return new EditorTreeModel(contextId, roots, visible, CountSelectable(roots));
    }

    public static HashSet<string> DefaultExpanded(EditorTreeModel model)
    {
        var expanded = new HashSet<string>(StringComparer.Ordinal);
        foreach (var root in model.Roots)
            expanded.Add(root.Key);
        return expanded;
    }

    public static bool ExpandForSelection(EditorTreeModel model, string selected, ISet<string> expanded)
    {
        if (selected.Length == 0)
            return false;

        foreach (var root in model.Roots)
        {
            if (ExpandForSelection(root, selected, expanded))
                return true;
        }
        return false;
    }

    private static bool ExpandForSelection(EditorTreeNode node, string selected, ISet<string> expanded)
    {
        var found = node.Id == selected;
        foreach (var child in node.Children)
            found |= ExpandForSelection(child, selected, expanded);

        if (found && node.HasChildren)
            expanded.Add(node.Key);
        return found;
    }

    private static void AppendVisible(
        EditorTreeNode node,
        string search,
        ISet<string>? expanded,
        List<EditorTreeNode> visible
    )
    {
        var searchMode = search.Length > 0;
        if (searchMode && !HasMatch(node, search))
            return;

        visible.Add(node);
        if (!node.HasChildren || (!searchMode && expanded?.Contains(node.Key) != true))
            return;

        foreach (var child in node.Children)
            AppendVisible(child, search, expanded, visible);
    }

    private static bool HasMatch(EditorTreeNode node, string search)
    {
        if (node.Matches(search))
            return true;
        foreach (var child in node.Children)
            if (HasMatch(child, search))
                return true;
        return false;
    }

    private static int CountSelectable(IEnumerable<EditorTreeNode> nodes)
    {
        var count = 0;
        foreach (var node in nodes)
        {
            if (node.Selectable)
                count++;
            count += CountSelectable(node.Children);
        }
        return count;
    }
}
