using WTT.Campaigns.Client.Authoring;
using WTT.Campaigns.Client.Authoring.Controllers;
using WTT.Campaigns.Client.Authoring.Scenes;
using WTT.Campaigns.Shared.Seasons;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Tests;

internal static class EditorControllerChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var layout = new MapLayout
        {
            Id = "layout",
            Name = "Original",
            Start = new() { Id = "start" },
            Exit = new() { Id = "exit" },
            Checkpoints = [new() { Id = "first" }, new() { Id = "second" }],
            SpawnPoints = [new() { Id = "spawn" }],
            PatrolRoutes = [new() { Id = "route", Waypoints = [new() { Id = "waypoint" }] }],
            Encounters =
            [
                new()
                {
                    Id = "encounter",
                    Name = "Original encounter",
                    Trigger = new() { Volume = new() { Id = "trigger" } },
                    Waves =
                    [
                        new()
                        {
                            Id = "wave",
                            Roster =
                            [
                                new()
                                {
                                    Id = "roster",
                                    SpawnPointIds = ["spawn"],
                                    PatrolRouteId = "route",
                                },
                            ],
                        },
                    ],
                },
            ],
        };
        check(!default(EditorAiSelection).Valid, "An absent AI selection is safe");
        foreach (
            var id in new[]
            {
                "enc:encounter",
                "trigger:encounter",
                "wave:encounter:wave",
                "roster:encounter:wave:roster",
                "spawn:spawn",
                "route:route",
                "waypoint:route:waypoint",
            }
        )
            check(
                EditorAiSelection.TryResolve(layout, id, out var selected) && selected.Valid,
                "AI resolves the current typed record: " + id
            );
        foreach (var id in new[] { "", "enc:missing", "waypoint:route:missing", "roster:encounter:wave:missing", "unknown:encounter" })
            check(
                !EditorAiSelection.TryResolve(layout, id, out var selected) && !selected.Valid,
                "Missing AI selection fails without retaining the previous record: " + id
            );

        var replacement = SeasonCompiler.Copy(layout);
        EditorAiSelection.TryResolve(replacement, "enc:encounter", out var current);
        current.Rename("  Changed  ");
        check(
            replacement.Encounters[0].Name == "Changed" && layout.Encounters[0].Name == "Original encounter",
            "Edits resolve against the replacement definition rather than the old object"
        );
        var undone = SeasonCompiler.Copy(layout);
        EditorAiSelection.TryResolve(undone, "enc:encounter", out current);
        check(current.Name == "Original encounter", "Undo resolves selection against the restored definition");
        var redone = SeasonCompiler.Copy(replacement);
        EditorAiSelection.TryResolve(redone, "enc:encounter", out current);
        check(current.Name == "Changed", "Redo resolves selection against the changed definition");
        check(
            !EditorAiSelection.TryResolve(new MapLayout(), "enc:encounter", out _),
            "Changing layouts cannot retain another layout's selection"
        );
        var definition = new SeasonDefinition { MapLayouts = [SeasonCompiler.Copy(layout)], Missions = [new() { LayoutId = layout.Id }] };
        var session = new RaidEditorSession("woods") { Definition = definition, Baseline = SeasonCompiler.Copy(definition) };
        session.Edit(d =>
        {
            EditorAiSelection.TryResolve(d.MapLayouts.Single(), "enc:encounter", out var selection);
            selection.Rename("Session edit");
        });
        session.Undo(false);
        EditorAiSelection.TryResolve(session.Definition!.MapLayouts.Single(), "enc:encounter", out current);
        check(current.Name == "Original encounter", "Actual session undo replaces the edited selection's definition");
        session.Undo(true);
        EditorAiSelection.TryResolve(session.Definition!.MapLayouts.Single(), "enc:encounter", out current);
        check(current.Name == "Session edit", "Actual session redo resolves the selected record in its restored definition");
        session.Edit(d => EditorMapRecords.Remove(d.MapLayouts.Single(), "first"));
        session.Undo(false);
        check(session.Definition!.MapLayouts.Single().Checkpoints[0].Id == "first", "Map record deletion participates in session undo");
        foreach (var id in new[] { "wave:encounter:wave", "spawn:spawn", "route:route", "waypoint:route:waypoint", "trigger:encounter" })
        {
            EditorAiSelection.TryResolve(replacement, id, out current);
            current.Rename("  Edited  ");
            check(current.Name == "Edited", "AI rename edits its resolved target: " + id);
        }

        var copy = EditorMapRecords.DuplicateLayout(layout);
        var originalIds = MapLayoutRules.OwnedIds(layout).ToHashSet();
        check(!MapLayoutRules.OwnedIds(copy).Any(originalIds.Contains), "Duplicated layout owns fresh record identifiers");
        var roster = copy.Encounters[0].Waves[0].Roster[0];
        check(
            roster.SpawnPointIds.Single() == copy.SpawnPoints[0].Id && roster.PatrolRouteId == copy.PatrolRoutes[0].Id,
            "Duplicated AI assignments point to the copied layout records"
        );
        check(copy.Name == "Original copy" && layout.Name == "Original", "Layout duplication leaves the source untouched");
        var checkpoint = EditorMapRecords.DuplicateVolume(layout, layout.Checkpoints[0]);
        check(
            layout.Checkpoints.Select(p => p.Id).SequenceEqual(new[] { "first", checkpoint.Id, "second" }),
            "Copied checkpoint is inserted immediately after its source"
        );
        EditorMapRecords.ReorderCheckpoint(layout, checkpoint.Id, 1);
        check(
            layout.Checkpoints.Select(p => p.Id).SequenceEqual(new[] { "first", "second", checkpoint.Id }),
            "Route reorder moves the selected checkpoint only"
        );
        EditorMapRecords.ReorderCheckpoint(layout, checkpoint.Id, 1);
        EditorMapRecords.ReorderCheckpoint(layout, "missing", -1);
        check(layout.Checkpoints.Last() == checkpoint, "Invalid route reorder leaves the route unchanged");
        EditorMapRecords.Remove(layout, checkpoint.Id);
        check(
            layout.Checkpoints.Select(p => p.Id).SequenceEqual(new[] { "first", "second" }),
            "Deleting a copied checkpoint preserves surrounding route order"
        );
        EditorMapRecords.Remove(layout, "start");
        check(layout.Start == null && layout.Exit?.Id == "exit", "Deleting the start marker preserves the exit");

        var requests = new SceneCatalogRequestState();
        var oldRequest = requests.Begin("old search");
        var newRequest = requests.Begin("new search");
        check(
            !requests.IsCurrent(oldRequest, "old search") && !requests.Finish(oldRequest, "old search") && requests.Loading,
            "An old response or error cannot publish or finish the current catalog request"
        );
        check(!requests.IsCurrent(newRequest, "different filter"), "Catalog responses must match both generation and filter/page key");
        check(requests.Finish(newRequest, "new search") && !requests.Loading, "The current response can finish its catalog request");
        requests.Reset();
        check(
            !requests.IsCurrent(newRequest, "new search") && requests.Key == "" && !requests.Loading,
            "Session reset invalidates pending catalog responses and loading state"
        );
        var repeated = requests.Begin("new search");
        check(
            repeated != newRequest && !requests.IsCurrent(newRequest, "new search"),
            "Repeating a search after reset cannot accept its previous response"
        );

        using var placement = new SceneOperationLifetime();
        var first = placement.Begin();
        var second = placement.Begin();
        check(
            first.IsCancellationRequested && !placement.IsCurrent(first) && placement.IsCurrent(second),
            "Replacing placement cancels the old model load and retains the current operation"
        );
        placement.Cancel();
        placement.Cancel();
        check(
            second.IsCancellationRequested && !placement.Active && !placement.IsCurrent(second),
            "Escape/close cancellation is repeatable and rejects late model results"
        );
        var third = placement.Begin();
        placement.Dispose();
        check(third.IsCancellationRequested && !placement.Active, "Controller disposal cancels pending placement");
    }
}
