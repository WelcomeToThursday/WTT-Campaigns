using WTT.Campaigns.Shared.Spatial;
using WTT.Campaigns.UI.Controls;

namespace WTT.Campaigns.Client.Authoring.Views;

// Browser adapters describe ownership; the shared tree handles presentation,
// filtering and expansion without needing to know any of these record types.
internal static class EditorLibraryTrees
{
    internal static IReadOnlyList<EditorTreeNode> Routes(IEnumerable<MapLayout> layouts, string location)
    {
        var roots = new List<EditorTreeNode>();
        foreach (var layout in layouts)
        {
            if (layout == null || layout.Location != location)
                continue;
            var label = "LAYOUT · " + layout.Name;
            var root = new EditorTreeNode("layout:" + layout.Id, label, layout.Id, true);
            roots.Add(root);
            void Add(SpatialCapture? point, string caption)
            {
                if (point != null)
                    root.Children.Add(
                        new EditorTreeNode("marker:" + layout.Id + ":" + point.Id, caption, point.Id, true, 1, label + " / " + caption)
                    );
            }
            Add(layout.Start, "START · " + layout.Start?.Name);
            for (var i = 0; i < layout.Checkpoints.Count; i++)
                Add(layout.Checkpoints[i], $"{i + 1}. {layout.Checkpoints[i]?.Name}");
            Add(layout.Exit, "EXIT · " + layout.Exit?.Name);
        }
        return roots;
    }

    internal static string? RouteOwner(IEnumerable<MapLayout> layouts, string location, string recordId)
    {
        foreach (var layout in layouts)
        {
            if (layout == null || layout.Location != location)
                continue;
            if (layout.Id == recordId || layout.Start?.Id == recordId || layout.Exit?.Id == recordId)
                return layout.Id;
            foreach (var checkpoint in layout.Checkpoints)
                if (checkpoint?.Id == recordId)
                    return layout.Id;
        }
        return null;
    }

    // The caller supplies the existing visibility-filtered zones. Grouping must
    // not broaden the active layout's editing scope.
    internal static IReadOnlyList<EditorTreeNode> Zones(IEnumerable<SeasonZone> zones, IEnumerable<MapLayout> layouts)
    {
        var roots = new List<EditorTreeNode>();
        var owners = new Dictionary<string, EditorTreeNode>(StringComparer.Ordinal);
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var layout in layouts)
            if (layout != null)
                names[layout.Id] = layout.Name;
        var shared = new EditorTreeNode("zones:shared", "SHARED");
        roots.Add(shared);
        owners.Add("", shared);
        foreach (var zone in zones)
        {
            if (zone == null)
                continue;
            var ownerId = zone.LayoutId ?? "";
            if (!owners.TryGetValue(ownerId, out var owner))
            {
                var label = "LAYOUT · " + (names.TryGetValue(ownerId, out var name) ? name : ownerId);
                owner = new EditorTreeNode("zones:layout:" + ownerId, label);
                owners.Add(ownerId, owner);
                roots.Add(owner);
            }
            owner.Children.Add(new EditorTreeNode("zone:" + zone.Id, zone.Name, zone.Id, true, 1, owner.Label + " / " + zone.Name));
        }
        return roots;
    }
}
