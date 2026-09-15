using Newtonsoft.Json;
using WTT.Campaigns.UI.Controls;

namespace WTT.Campaigns.Tests;

internal static class EditorDockChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var ids = new[]
        {
            "Tool:Layouts",
            "Tool:Routes",
            "Tool:Zones",
            "Tool:Bindings",
            "Tool:Captures",
            "Tool:Scene",
            "Tool:AI",
            "Inspector",
            "EnvironmentMenu",
            "Controls",
        }.ToHashSet();
        var root = EditorDockNode.Default();
        var left = EditorDockLayout.Nodes(root).First(n => n.Tabs.Contains("Tool:Layouts"));
        foreach (var id in ids.Where(t => t.StartsWith("Tool:") && t != "Tool:Layouts"))
            root = EditorDockLayout.Dock(root, id, left.Id, "center");
        check(EditorDockLayout.Valid(root, ids), "All tools coexist in a valid dock layout");
        check(left.Tabs.Length == 7, "Opening categories retains every existing category");
        root = EditorDockLayout.Dock(root, "Tool:AI", left.Id, "center", 0);
        check(
            left.Tabs[0] == "Tool:AI" && left.Active == "Tool:AI" && left.Tabs.Distinct().Count() == 7,
            "Tab reorder preserves unique windows and activates the moved tab"
        );
        root = EditorDockLayout.Dock(root, "Tool:Routes", left.Id, "bottom");
        check(EditorDockLayout.Nodes(root).Any(n => n.Kind == "vertical"), "Dropping below a dock produces a nested vertical split");
        root = EditorDockLayout.Dock(root, "EnvironmentMenu", root.Id, "bottom");
        root = EditorDockLayout.Dock(root, "Controls", root.Id, "right");
        check(EditorDockLayout.Valid(root, ids), "Auxiliary windows use the same split and tab model");
        var saved = JsonConvert.SerializeObject(new EditorWindowLayout { Dock = root });
        var restored = JsonConvert.DeserializeObject<EditorWindowLayout>(saved)!;
        check(EditorDockLayout.Valid(restored.Dock!, ids), "Nested dock layout survives serialization");
        check(
            EditorDockLayout.Nodes(restored.Dock).Select(n => n.Id).SequenceEqual(EditorDockLayout.Nodes(root).Select(n => n.Id)),
            "Saved node identities survive restore"
        );

        foreach (var (width, height) in new[] { (1280, 720), (1920, 1080), (3440, 1440) })
        foreach (var bottom in new[] { 40, 88 })
        {
            var area = new EditorDockRect(56, 84, width - 64, height - 84 - bottom);
            var copy = JsonConvert.DeserializeObject<EditorDockNode>(JsonConvert.SerializeObject(root))!;
            var fitted = EditorDockLayout.FitDisplay(copy, area, ids);
            var rectangles = EditorDockLayout.Arrange(fitted, area, ids);
            foreach (var n in EditorDockLayout.Nodes(fitted))
            {
                if (!rectangles.TryGetValue(n.Id, out var r))
                    continue;
                check(
                    r.X >= 56 && r.Y >= 84 && r.X + r.Width <= width - 7.9f && r.Y + r.Height <= height - bottom + .1f,
                    "Dock layout avoids toolbars and stays inside the available display"
                );
                if (n.Kind == "viewport")
                    check(r.Width >= 320 && r.Height >= 180, "Responsive docking preserves a usable scene viewport");
                if (n.Kind == "tabs")
                    check(r.Width >= 280 && r.Height >= 206, "Responsive docking preserves readable tool minimums");
            }
            check(EditorDockLayout.Valid(fitted, ids), "Display fallback retains a valid dock tree");
        }

        var floated = EditorDockLayout.Remove(root, "Tool:Routes")!;
        check(!EditorDockLayout.Nodes(floated).Any(n => n.Tabs.Contains("Tool:Routes")), "Dragging a tool out removes its dock membership");
        check(EditorDockLayout.Valid(floated, ids), "Undocking collapses empty splits safely");
        var viewport = EditorDockLayout.Nodes(floated).First(n => n.Kind == "viewport");
        var before = JsonConvert.SerializeObject(floated);
        floated = EditorDockLayout.Dock(floated, "Tool:Routes", viewport.Id, "center");
        check(before == JsonConvert.SerializeObject(floated), "Center drops cannot replace the scene viewport with a tab");
        var bad = EditorDockNode.Default();
        bad.Ratio = float.NaN;
        check(!EditorDockLayout.Valid(bad, ids), "Non-finite split ratios are rejected");
        bad = EditorDockNode.Default();
        bad.First = bad;
        check(!EditorDockLayout.Valid(bad, ids), "Cyclic dock graphs fail validation without recursion overflow");
        bad = EditorDockNode.Split("horizontal", EditorDockNode.Group("Tool:AI", "Tool:AI"), new() { Kind = "viewport" });
        check(!EditorDockLayout.Valid(bad, ids), "A window cannot appear in two tabs");

        var defaults = new EditorWindowLayout
        {
            Windows = ids.Select(id => new EditorWindowPlacement
                {
                    Id = id,
                    Width = 360,
                    Height = 400,
                })
                .ToArray(),
            Dock = EditorDockNode.Default(),
        };
        var legacy = new EditorWindowLayout
        {
            Version = 1,
            Windows = new[]
            {
                new EditorWindowPlacement
                {
                    Id = "Library",
                    Width = 512,
                    Height = 390,
                    X = -.25f,
                    Visible = true,
                },
                new EditorWindowPlacement
                {
                    Id = "Inspector",
                    Width = 405,
                    Height = 490,
                    X = .25f,
                    Visible = false,
                },
            },
        };
        var migrated = EditorWindowLayout.Restore(legacy, defaults)!;
        var layouts = migrated.Windows.Single(p => p.Id == "Tool:Layouts");
        check(
            migrated.Version == 2 && layouts.Width == 512 && layouts.X == -.25f && layouts.Visible && layouts.ManualSize,
            "Legacy browser becomes Layouts and preserves manual placement"
        );
        check(migrated.Windows.Single(p => p.Id == "Inspector").Width == 405, "Migration retains auxiliary placements");
        check(!EditorDockLayout.Nodes(migrated.Dock).Any(n => n.Tabs.Contains("Tool:Layouts")), "Migrated floating browser stays floating");
        check(EditorWindowLayout.Restore(new() { Dock = bad }, defaults) == null, "Invalid saved layouts fall back to defaults");
        check(EditorWindowLayout.Restore(new() { Version = 99 }, defaults) == null, "Unknown layout versions do not damage defaults");
        check(defaults.Windows.All(p => p.Width == 360), "Migration leaves default placements unchanged");

        var closedFloat = new EditorWindowLayout
        {
            Dock = EditorDockNode.Default(),
            Windows = new[]
            {
                new EditorWindowPlacement
                {
                    Id = "Tool:AI",
                    Opened = true,
                    Visible = false,
                    ManualSize = true,
                    Width = 432,
                    Height = 321,
                    X = .1f,
                    Y = -.1f,
                },
            },
        };
        var roundtrip = JsonConvert.DeserializeObject<EditorWindowLayout>(JsonConvert.SerializeObject(closedFloat))!;
        var recovered = EditorWindowLayout.Restore(roundtrip, defaults)!;
        var recoveredAi = recovered.Windows.Single(p => p.Id == "Tool:AI");
        check(
            recoveredAi.Opened && !recoveredAi.Visible && recoveredAi.ManualSize && recoveredAi.Width == 432 && recoveredAi.Height == 321,
            "A closed floating window retains its opening history and preferred size after restart"
        );
        check(
            !EditorDockLayout.Nodes(recovered.Dock).Any(n => n.Tabs.Contains("Tool:AI")),
            "Closed floating windows do not rejoin a default dock during restore"
        );

        var repeated = EditorDockNode.Default();
        for (var move = 0; move < 100; move++)
        {
            var owner = EditorDockLayout.Nodes(repeated).First(n => n.Tabs.Contains("Tool:Layouts"));
            repeated = EditorDockLayout.Dock(repeated, "Tool:AI", owner.Id, "bottom");
            owner = EditorDockLayout.Nodes(repeated).First(n => n.Tabs.Contains("Tool:Layouts"));
            repeated = EditorDockLayout.Dock(repeated, "Tool:AI", owner.Id, "center");
        }
        check(
            EditorDockLayout.Valid(repeated, ids) && EditorDockLayout.Nodes(repeated).Count() == 5,
            "Repeated split and tab moves collapse empty branches without growing the saved layout"
        );

        var ai = new EditorToolSession<object>
        {
            Selection = "enc:1",
            Page = 3,
            Picked = new(),
        };
        var zones = new EditorToolSession<object> { Selection = "zone:1", Page = 1 };
        ai.Rows.Add(("enc:1", "Encounter"));
        zones.Rows.Add(("zone:1", "Zone"));
        zones.Rows.Clear();
        zones.Page = 0;
        check(
            ai.Rows.Count == 1 && ai.Page == 3 && ai.Selection == "enc:1",
            "Refreshing one tool leaves another tool's records and selection intact"
        );
        ai.Reconcile(id => id == "enc:1");
        check(ai.Selection == "enc:1" && ai.Picked != null, "Existing selections survive a content refresh");
        ai.Reconcile(_ => false);
        check(
            ai.Selection == "" && ai.Picked == null && zones.Selection == "zone:1",
            "Deletion clears only the affected tool's stale selection and scene target"
        );

        var nodes = Enumerable.Range(0, 2000).Select(i => new EditorTreeNode("item:" + i, "Spawn " + i, "spawn:" + i, true)).ToArray();
        var model = EditorTreeModel.Create("large", nodes, "", new HashSet<string>());
        check(model.Visible.Count == 2000, "Large compact trees retain every record without pagination");
        model = EditorTreeModel.Create("large", nodes, "spawn:1999", new HashSet<string>());
        check(model.Visible.Count == 1 && model.Visible[0].Id == "spawn:1999", "Large tree search keeps stable record identity");
    }
}
