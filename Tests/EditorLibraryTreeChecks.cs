using WTT.Campaigns.Client.Authoring.Views;
using WTT.Campaigns.Shared.Spatial;
using WTT.Campaigns.UI.Controls;

namespace WTT.Campaigns.Tests;

internal static class EditorLibraryTreeChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var first = new MapLayout
        {
            Id = "first",
            Name = "First route",
            Location = "woods",
            Start = new SpatialCapture { Id = "start", Name = "Start" },
            Checkpoints = new()
            {
                new() { Id = "z", Name = "First checkpoint" },
                new() { Id = "a", Name = "Second checkpoint" },
            },
            Exit = new MapVolume { Id = "exit", Name = "Exit" },
        };
        var second = new MapLayout
        {
            Id = "second",
            Name = "Second route",
            Location = "woods",
            Checkpoints = new()
            {
                new() { Id = "other-marker", Name = "Other marker" },
            },
        };
        var foreign = new MapLayout { Id = "foreign", Location = "factory4_day" };
        var layouts = new[] { first, second, foreign };
        var routes = EditorLibraryTrees.Routes(layouts, "woods");
        check(routes.Select(n => n.Id).SequenceEqual(new[] { "first", "second" }), "Route tree stays on the connected map");
        check(
            routes[0].Children.Select(n => n.Id).SequenceEqual(new[] { "start", "z", "a", "exit" }),
            "Route tree preserves start, authored checkpoint order, and exit without sorting IDs"
        );
        check(
            routes[0].Children.All(n => n.Selectable && n.Depth == 1 && n.Path.Contains("First route")),
            "Route children retain selectable IDs and searchable owner context"
        );
        check(
            EditorLibraryTrees.RouteOwner(layouts, "woods", "other-marker") == "second",
            "Selecting a marker from another route resolves its owning layout"
        );
        check(
            EditorLibraryTrees.RouteOwner(layouts, "woods", "foreign") == null
                && EditorLibraryTrees.RouteOwner(layouts, "woods", "deleted") == null,
            "Route selection rejects foreign-map and deleted records"
        );
        var collapsed = new HashSet<string>(StringComparer.Ordinal);
        var search = EditorTreeModel.Create("routes:woods", routes, "Second checkpoint", collapsed);
        check(
            search.Visible.Select(n => n.Id).SequenceEqual(new[] { "first", "a" }),
            "Route search reveals the matching marker with its owning layout"
        );
        check(
            collapsed.Count == 0 && EditorTreeModel.Create("routes:woods", routes, "", collapsed).Visible.Count == 2,
            "Clearing route search restores collapsed layouts"
        );
        EditorTreeModel.ExpandForSelection(search, "other-marker", collapsed);
        check(
            EditorTreeModel.Create("routes:woods", routes, "", collapsed).Visible.Any(n => n.Id == "other-marker"),
            "Selection from another route reveals that route's marker"
        );
        var shared = new SeasonZone
        {
            Id = "shared",
            Name = "Shared zone",
            Location = "woods",
        };
        var owned = new SeasonZone
        {
            Id = "owned",
            Name = "Owned zone",
            Location = "woods",
            LayoutId = "first",
        };
        var zones = EditorLibraryTrees.Zones(new[] { shared, owned }, layouts);
        check(
            zones.Count == 2
                && zones.All(n => !n.Selectable)
                && zones[0].Children.Single().Id == "shared"
                && zones[1].Children.Single().Id == "owned",
            "Zone tree separates nonselectable ownership groups from editable zones"
        );
        check(
            zones[1].Label.Contains("First route") && zones[1].Children[0].Path.Contains("First route"),
            "Zone tree uses the layout name for owner context"
        );
        check(
            EditorLibraryTrees.Zones(new[] { shared }, layouts).SelectMany(n => n.Children).All(n => n.Id == "shared"),
            "Zone tree does not introduce zones outside the caller's existing visibility filter"
        );
    }
}
