using WTT.Campaigns.Client.Authoring.Views;
using WTT.Campaigns.Shared.Spatial;
using WTT.Campaigns.UI.Controls;

namespace WTT.Campaigns.Tests;

internal static class AiTreeChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var layout = new MapLayout
        {
            Id = "layout-a",
            Name = "Woods encounter layout",
            Location = "woods",
            Encounters = new()
            {
                new MapEncounter
                {
                    Id = "enc-a",
                    Name = "Bridge ambush",
                    Trigger = new MapEncounterTrigger
                    {
                        Type = MapEncounterTrigger.PlayerEntry,
                        Volume = new MapVolume { Id = "trigger-a", Name = "Bridge trigger" },
                    },
                    Waves = new()
                    {
                        new MapEncounterWave
                        {
                            Id = "wave-a",
                            Name = "First wave",
                            Roster = new()
                            {
                                new MapEncounterRosterEntry
                                {
                                    Id = "roster-a",
                                    Role = "pmcUSEC",
                                    Count = 2,
                                },
                            },
                        },
                        new MapEncounterWave
                        {
                            Id = "wave-b",
                            Name = "Second wave",
                            Roster = new()
                            {
                                new MapEncounterRosterEntry
                                {
                                    Id = "roster-b",
                                    Role = "assault",
                                    Count = 3,
                                },
                            },
                        },
                    },
                },
            },
            SpawnPoints = new()
            {
                new SpatialCapture { Id = "spawn-a", Name = "Bridge spawn" },
            },
            PatrolRoutes = new()
            {
                new MapPatrolRoute
                {
                    Id = "patrol-a",
                    Name = "Bridge sweep",
                    Waypoints = new()
                    {
                        new SpatialCapture { Id = "waypoint-a", Name = "North" },
                        new SpatialCapture { Id = "waypoint-b", Name = "South" },
                    },
                },
            },
        };
        var otherLayout = new MapLayout
        {
            Id = "layout-b",
            Name = "Factory encounter layout",
            Location = "factory4_day",
            Encounters = new()
            {
                new MapEncounter { Id = "enc-b", Name = "Factory alarm" },
            },
        };

        var collapsed = new HashSet<string>(StringComparer.Ordinal);
        var rootsOnly = RaidEditorAiTree.Build(layout, "", collapsed);
        check(rootsOnly.Roots.Count == 3 && rootsOnly.Roots.All(root => !root.Selectable), "AI tree has nonselectable family groups");
        check(rootsOnly.Visible.Count == 3, "AI tree starts with collapsed families");

        var expanded = EditorTreeModel.DefaultExpanded(rootsOnly);
        var families = RaidEditorAiTree.Build(layout, "", expanded);
        check(
            families.Visible.Select(node => node.Id).SequenceEqual(new[] { "", "enc:enc-a", "", "spawn:spawn-a", "", "route:patrol-a" }),
            "AI tree keeps encounter, spawn and patrol families continuous in source order"
        );
        expanded.Add("enc:enc-a");
        expanded.Add("wave:enc-a:wave-a");
        expanded.Add("route:patrol-a");
        var full = RaidEditorAiTree.Build(layout, "", expanded);
        check(
            full.Visible.Select(node => node.Id)
                .SequenceEqual(
                    new[]
                    {
                        "",
                        "enc:enc-a",
                        "trigger:enc-a",
                        "wave:enc-a:wave-a",
                        "roster:enc-a:wave-a:roster-a",
                        "wave:enc-a:wave-b",
                        "",
                        "spawn:spawn-a",
                        "",
                        "route:patrol-a",
                        "waypoint:patrol-a:waypoint-a",
                        "waypoint:patrol-a:waypoint-b",
                    }
                ),
            "AI tree preserves trigger, wave, roster and ordered waypoint nesting"
        );
        check(
            full.Visible.Single(node => node.Id == "roster:enc-a:wave-a:roster-a").Path.Contains("Bridge ambush"),
            "AI search paths retain every ancestor label"
        );

        var search = new HashSet<string>(StringComparer.Ordinal);
        MapPatrolRouteEditing.Reverse(layout.PatrolRoutes[0]);
        var reordered = RaidEditorAiTree.Build(layout, "", expanded);
        check(
            reordered.Visible.Single(n => n.Id == "waypoint:patrol-a:waypoint-b").Label == "WAYPOINT 1 · South",
            "Reordering refreshes displayed waypoint numbers while preserving names and selection IDs"
        );
        MapPatrolRouteEditing.Reverse(layout.PatrolRoutes[0]);
        var searchModel = RaidEditorAiTree.Build(layout, "roster-a", search);
        check(
            searchModel
                .Visible.Select(node => node.Id)
                .SequenceEqual(new[] { "", "enc:enc-a", "wave:enc-a:wave-a", "roster:enc-a:wave-a:roster-a" }),
            "AI search reveals matching descendants with their collapsed ancestors"
        );
        check(search.Count == 0, "AI search does not mutate persisted foldout state");

        expanded.Remove("enc:enc-a");
        check(
            RaidEditorAiTree.Build(layout, "", expanded).Visible.All(node => !node.Id.StartsWith("wave:", StringComparison.Ordinal)),
            "Collapsing an encounter hides its waves and rosters"
        );
        check(
            RaidEditorAiTree.Build(layout, "roster-a", expanded).Visible.Any(node => node.Id == "roster:enc-a:wave-a:roster-a")
                && RaidEditorAiTree.Build(layout, "", expanded).Visible.All(node => !node.Id.StartsWith("wave:", StringComparison.Ordinal)),
            "Clearing search restores the persisted collapsed encounter"
        );

        var selectionExpanded = new HashSet<string>(StringComparer.Ordinal);
        var selectionModel = RaidEditorAiTree.Build(layout, "", selectionExpanded);
        check(
            EditorTreeModel.ExpandForSelection(selectionModel, "roster:enc-a:wave-a:roster-a", selectionExpanded),
            "External AI child selection finds its path"
        );
        check(
            RaidEditorAiTree.Build(layout, "", selectionExpanded).Visible.Any(node => node.Id == "roster:enc-a:wave-a:roster-a"),
            "External AI child selection expands its encounter and wave ancestors"
        );

        var isolated = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            [layout.Id] = new HashSet<string>(StringComparer.Ordinal) { "group:encounters" },
            [otherLayout.Id] = new HashSet<string>(StringComparer.Ordinal) { "group:encounters" },
        };
        isolated[layout.Id].Clear();
        check(
            RaidEditorAiTree.Build(otherLayout, "", isolated[otherLayout.Id]).Visible.Any(node => node.Id == "enc:enc-b")
                && RaidEditorAiTree.Build(layout, "", isolated[layout.Id]).Visible.Count == 3,
            "AI foldout state stays isolated per layout"
        );
        check(full.SelectableCount == 10, "AI tree counts selectable records while excluding family groups");
    }
}
