using System.Numerics;
using Newtonsoft.Json;
using WTT.Campaigns.Client.Authoring;
using WTT.Campaigns.Client.Authoring.Navigation;
using WTT.Campaigns.Shared.Seasons;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Tests;

internal static class NavigationPaintingChecks
{
    internal static void Run(Action<bool, string> check)
    {
        Vector3? Ramp(Vector3 p) => new Vector3(p.X, p.X * .4f + p.Z * .3f, p.Z);
        var contour = NavigationPaintContour.Build(0, 0, .35f, Ramp);
        check(
            contour.Indices.Length == 96 && contour.Vertices.All(v => Math.Abs(v.Y - (v.X * .4f + v.Z * .3f)) < .00001f),
            "Paint overlay follows a sloped physical surface rather than the flat cell height"
        );
        var adjacent = NavigationPaintContour.Build(1, 0, .75f, Ramp);
        check(
            Enumerable.Range(0, 5).All(row => contour.Vertices[row * 5 + 4] == adjacent.Vertices[row * 5]),
            "Adjacent painted cells share identical terrain edges despite different centre heights"
        );
        var contourHole = NavigationPaintContour.Build(0, 0, 0, p => p.X == .5f && p.Z == .5f ? null : p);
        check(contourHole.Indices.Length < 96 && !contourHole.Indices.Contains(12), "Paint contours never bridge missing ground samples");
        var otherFloor = NavigationPaintContour.Build(0, 0, 0, p => p + new Vector3(0, 3, 0));
        check(otherFloor.Indices.Length == 0, "Paint contours cannot jump onto a stacked floor");
        var ledge = NavigationPaintContour.Build(0, 0, .5f, p => new Vector3(p.X, p.X < .5f ? 0 : 1, p.Z));
        check(
            Enumerable
                .Range(0, ledge.Indices.Length / 3)
                .All(t =>
                    ledge.Vertices[ledge.Indices[t * 3]].Y == ledge.Vertices[ledge.Indices[t * 3 + 1]].Y
                    && ledge.Vertices[ledge.Indices[t * 3]].Y == ledge.Vertices[ledge.Indices[t * 3 + 2]].Y
                ),
            "Paint display does not invent a ramp across a sharp ledge"
        );
        var healthy = NavigationHealthRules.Classify(true, true, true, 20, 45, false, .05f, .3f);
        check(healthy == NavigationHealthIssue.None, "Supported clear connected sample has no diagnostic issue");
        var issues = NavigationHealthRules.Classify(true, false, false, 50, 45, true, .5f, .3f);
        check(
            issues
                == (
                    NavigationHealthIssue.Clearance
                    | NavigationHealthIssue.Disconnected
                    | NavigationHealthIssue.Slope
                    | NavigationHealthIssue.Narrow
                    | NavigationHealthIssue.Height
                ),
            "Overlapping issues remain independently filterable"
        );
        issues = NavigationHealthRules.Classify(false, true, true, 0, 45, false, 9, .3f);
        check(issues == NavigationHealthIssue.Support, "Missing support is not misreported as a measured height mismatch");
        issues = NavigationHealthRules.Classify(true, true, true, 45, 45, false, .3f, .3f);
        check(issues == NavigationHealthIssue.None, "Native slope and step thresholds are inclusive");
        var recipe = new MapNavigationRecipe();
        MapNavigationPainting.Stamp(recipe, -2, 3, 10, "Add");
        MapNavigationPainting.Stamp(recipe, -2, 3, 14, "Block");
        check(recipe.Version == 2 && recipe.Cells.Count == 2, "Paint records negative coordinates and separate stacked floors");
        MapNavigationPainting.Stamp(recipe, -2, 3, 10.1f, "Block");
        check(
            recipe.Cells.Count == 2 && recipe.Cells.All(c => c.Mode == "Block"),
            "Block replaces only the selected floor's Add footprint"
        );
        MapNavigationPainting.Stamp(recipe, -2, 3, 10, "Erase");
        check(recipe.Cells.Count == 1 && recipe.Cells[0].Y == 14, "Erasing a lower floor preserves upper-floor navigation edits");
        MapNavigationPainting.Stamp(recipe, -2, 3, 14, "Erase");
        check(recipe.Cells.Count == 0, "Erasing a block removes the saved cut so native navigation can return");
        MapNavigationPainting.Stamp(recipe, 0, 0, 0, "Add");
        recipe.Connections.Add(
            new()
            {
                Start = new() { X = .1f },
                End = new() { X = 3 },
            }
        );
        var copy = JsonConvert.DeserializeObject<MapNavigationRecipe>(JsonConvert.SerializeObject(recipe))!;
        check(
            copy.Cells.Single().Mode == "Add" && copy.Connections.Single().End.X == 3,
            "Manual recipe round-trips cells and explicit connections"
        );
        check(MapNavigationRules.Errors(copy).Count == 0, "Valid manual recipe passes shared validation");
        copy.Version = 1;
        check(MapNavigationRules.Errors(copy).Count > 0, "Legacy recipe versions cannot silently accept paint");
        copy.Version = 2;
        copy.Connections[0].End.X = 100;
        check(MapNavigationRules.Errors(copy).Count > 0, "Long automatic-style connections are rejected");
        copy = SeasonCompiler.Copy(recipe);
        copy.Cells.Add(
            new()
            {
                X = 0,
                Z = 0,
                Y = .1f,
            }
        );
        check(MapNavigationRules.Errors(copy).Count > 0, "Duplicate same-floor cells are rejected");
        copy = SeasonCompiler.Copy(recipe);
        copy.Cells[0].Y = float.NaN;
        check(MapNavigationRules.Errors(copy).Count > 0, "Nonfinite painted floors are rejected");
        copy = SeasonCompiler.Copy(recipe);
        copy.Cells[0].Mode = "FloodFill";
        check(MapNavigationRules.Errors(copy).Count > 0, "No flood-fill or inferred expansion mode exists");
        copy = SeasonCompiler.Copy(recipe);
        copy.Cells = null!;
        check(MapNavigationRules.Errors(copy).Count > 0, "Null paint arrays fail closed");
        copy = SeasonCompiler.Copy(recipe);
        copy.Connections[0].Start = null!;
        check(MapNavigationRules.Errors(copy).Count > 0, "Null connection endpoints fail closed");
        recipe.Cells = Enumerable.Range(0, MapNavigationPainting.Limit).Select(i => new MapNavigationCell { X = i, Z = 0 }).ToList();
        var full = false;
        try
        {
            MapNavigationPainting.Stamp(recipe, -1, 0, 0, "Add");
        }
        catch (InvalidOperationException)
        {
            full = true;
        }
        check(full && recipe.Cells.Count == MapNavigationPainting.Limit, "Brush capacity failure preserves the previous footprint");
        MapNavigationPainting.Stamp(recipe, 0, 0, 0, "Block");
        check(recipe.Cells.Count == MapNavigationPainting.Limit, "Editing an existing cell remains possible at capacity");
        MapNavigationPainting.Stamp(recipe, 0, 0, 0, "Erase");
        check(recipe.Cells.Count == MapNavigationPainting.Limit - 1, "Erase works at paint capacity");

        var session = new RaidEditorSession("woods")
        {
            Definition = new() { MapLayouts = new() { MapEditorChecks.Example() } },
            Hold = true,
        };
        session.Baseline = SeasonCompiler.Copy(session.Definition);
        session.Edit(d =>
        {
            d.MapLayouts[0].Navigation = new();
            for (var x = 0; x < 8; x++)
                MapNavigationPainting.Stamp(d.MapLayouts[0].Navigation!, x, 0, 0, "Add");
        });
        check(session.Definition!.MapLayouts[0].Navigation!.Cells.Count == 8, "One editor stroke saves all painted cells atomically");
        session.Undo(false);
        check(session.Definition!.MapLayouts[0].Navigation == null, "One undo removes the complete stroke");
        session.Undo(true);
        check(session.Definition!.MapLayouts[0].Navigation!.Cells.Count == 8, "Redo restores the complete manual stroke");

        var a = new Vector3(-2, -2, -2);
        var b = new Vector3(3, 3, -2);
        var c = new Vector3(-2, -2, 3);
        var clipped = NavigationPaintGeometry.Box(a, b, c, new(0, -10, 0), new(1, 10, 1));
        check(
            clipped.Count >= 3 && clipped.All(p => p.X >= -.00001f && p.X <= 1.00001f && p.Z >= -.00001f && p.Z <= 1.00001f),
            "Clipping retains only the painted footprint"
        );
        check(
            clipped.All(p => Math.Abs(p.X - p.Y) < .00001f),
            "Clipping preserves physical ramp heights instead of inventing a horizontal floor"
        );
        var square = new List<Vector3> { new(0, 0, 0), new(4, 0, 0), new(4, 0, 4), new(0, 0, 4) };
        var outside = NavigationPaintGeometry.Subtract(square, new(1, 0, 1), new(3, 0, 1), new(1, 0, 3));
        check(
            Math.Abs(outside.Sum(NavigationPaintGeometry.Area) - 14) < .0001f,
            "Native triangle subtraction preserves the exact unoccupied area"
        );
        var reversed = NavigationPaintGeometry.Subtract(square, new(1, 0, 3), new(3, 0, 1), new(1, 0, 1));
        check(Math.Abs(reversed.Sum(NavigationPaintGeometry.Area) - 14) < .0001f, "Native subtraction works for either triangle winding");
        var removed = NavigationPaintGeometry.SubtractBox(square, new(-1, -1, -1), new(5, 1, 5));
        check(removed.Sum(NavigationPaintGeometry.Area) < .00001f, "A fully painted triangle leaves no unpainted remainder");
        var hole = NavigationPaintGeometry.SubtractBox(square, new(1, -1, 1), new(3, 1, 3));
        check(Math.Abs(hole.Sum(NavigationPaintGeometry.Area) - 12) < .0001f, "Box subtraction preserves holes instead of bridging them");
        var upper = NavigationPaintGeometry.SubtractBox(square, new(-1, 3, -1), new(5, 5, 5));
        check(
            Math.Abs(upper.Sum(NavigationPaintGeometry.Area) - 16) < .0001f,
            "Paint on an upper floor cannot certify a lower-floor triangle"
        );
        // A hole away from vertices, edge midpoints and triangle centroid still fails
        // exact coverage; sparse point tests would incorrectly accept this triangle.
        var triangle = new List<Vector3> { new(0, 0, 0), new(6, 0, 0), new(0, 0, 6) };
        var pieces = new List<List<Vector3>> { triangle };
        for (var x = 0; x < 6; x++)
        for (var z = 0; z < 6; z++)
        {
            if (x == 1 && z == 0)
                continue;
            pieces = pieces.SelectMany(p => NavigationPaintGeometry.SubtractBox(p, new(x, -1, z), new(x + 1, 1, z + 1))).ToList();
        }
        check(
            pieces.Sum(NavigationPaintGeometry.Area) > .99f,
            "Exact footprint validation catches an unpainted hole missed by sample points"
        );

        var coverage = new MapNavigationRecipe
        {
            Cells = new()
            {
                new()
                {
                    X = -201,
                    Z = 300,
                    Y = 12,
                },
                new()
                {
                    X = -200,
                    Z = 300,
                    Y = 12,
                },
            },
        };
        float Outside(Vector3 p, Vector3 q, Vector3 r) =>
            new NavigationPaintFootprint(coverage).Outside(p, q, r).Sum(NavigationPaintGeometry.Area);
        check(
            Outside(new(-201, 12, 300), new(-199, 12, 300), new(-200, 12, 301)) <= .0001f,
            "Actual footprint checker accepts adjacent painted cells at negative map coordinates"
        );
        check(
            Outside(new(-201, 12, 300), new(-199, 12, 300), new(-200, 12, 301.1f)) > .0001f,
            "Actual footprint checker rejects a real outer-edge spill without relaxing horizontal tolerance"
        );
        check(
            Outside(new(-201, 12.1f, 300), new(-199, 12.1f, 300), new(-200, 12.1f, 301)) <= .0001f,
            "Ordinary floor mesh lift within the existing floor band is accepted"
        );
        check(
            Outside(new(-201, 13, 300), new(-199, 13, 300), new(-200, 13, 301)) > .99f,
            "Actual footprint checker rejects a stacked surface outside the selected floor band"
        );
        coverage.Cells[1].Mode = "Block";
        check(Outside(new(-201, 12, 300), new(-199, 12, 300), new(-200, 12, 301)) > .49f, "Block cells never certify added navigation");
        coverage.Cells = new();
        for (var x = 0; x < 6; x++)
        for (var z = 0; z < 6; z++)
            if (x != 1 || z != 0)
                coverage.Cells.Add(new() { X = x, Z = z });
        check(
            Outside(new(0, 0, 0), new(6, 0, 0), new(0, 0, 6)) > .99f,
            "Runtime footprint checker rejects an interior hole even when vertices and centroid are painted"
        );
    }
}
