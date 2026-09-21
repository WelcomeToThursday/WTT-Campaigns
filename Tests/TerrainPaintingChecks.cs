using Newtonsoft.Json;
using WTT.Campaigns.Client.Authoring;
using WTT.Campaigns.Shared.Seasons;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Tests;

internal static class TerrainPaintingChecks
{
    private static MapTerrainRecipe Recipe() =>
        new()
        {
            Target = new()
            {
                Scene = "woods",
                Path = "Terrain[0]",
                Fingerprint = new string('A', 64),
                Width = 100,
                Depth = 50,
                Textures = 3,
                Grass = 2,
            },
            Strokes = new()
            {
                new()
                {
                    Layer = 1,
                    Points = new()
                    {
                        new() { X = 25, Z = 10 },
                    },
                },
            },
        };

    internal static void Run(Action<bool, string> check)
    {
        var layout = MapEditorChecks.Example();
        check(!JsonConvert.SerializeObject(layout).Contains("\"Terrain\""), "Legacy layouts omit terrain recipes");
        layout.Terrain = new() { Recipe() };
        check(MapTerrainPainting.Errors(layout.Terrain).Count == 0, "A bound terrain brush validates");
        check(MapLayoutRules.Format(new[] { layout }) == 14, "Terrain painting promotes content to format 14");
        var copy = SeasonCompiler.Copy(layout);
        copy.Terrain![0].Strokes[0].Points[0].X = 30;
        check(layout.Terrain[0].Strokes[0].Points[0].X == 25, "Paint copies isolate undo and replay inputs");
        var stroke = layout.Terrain[0].Strokes[0];
        var center = stroke.Points[0];
        check(MapTerrainPainting.Weight(25, 10, center, stroke) == .5f, "Brush center respects strength");
        check(
            MapTerrainPainting.Weight(27, 10, center, stroke) == 0 && MapTerrainPainting.Weight(28, 10, center, stroke) == 0,
            "Brush edge and outside samples are untouched"
        );
        check(MapTerrainPainting.Weight(26.5f, 10, center, stroke) == .25f, "Soft brush edge uses smooth falloff");
        var region = MapTerrainPainting.Region(new() { X = 0, Z = 49 }, 2, 100, 50, 200, 50);
        check(region == (0, 47, 4, 3), "Non-square terrain converts X/Z independently and clips to the tile");
        var weights = new[] { .8f, .1f, .1f };
        var original = (float[])weights.Clone();
        MapTerrainPainting.Blend(weights, original, 1, .5f, false);
        check(
            Math.Abs(weights[1] - .55f) < .00001f && Math.Abs(weights.Sum() - 1) < .00001f,
            "Texture painting redistributes all channels and preserves normalization"
        );
        check(MapTerrainPainting.Dominant(weights) == 1, "Native sound and impact lookup follows the painted dominant layer");
        MapTerrainPainting.Blend(weights, original, 1, 1, true);
        check(weights.SequenceEqual(original), "Full-strength restore recovers original texture weights");
        var empty = new float[3];
        MapTerrainPainting.Blend(empty, new float[3], 2, 1, true);
        check(empty.SequenceEqual(new[] { 0f, 0f, 1f }), "Degenerate weights cannot produce NaN or a zero total");
        stroke.Mode = "AddGrass";
        stroke.Density = 8;
        var density = MapTerrainPainting.GrassDensity(0, 0, .5f, stroke);
        check(
            density == 4 && MapTerrainPainting.GrassDensity(12, 12, 1, stroke) == 12,
            "Adding grass approaches target without thinning denser native grass"
        );
        stroke.Mode = "RemoveGrass";
        check(MapTerrainPainting.GrassDensity(8, 8, 1, stroke) == 0, "Removal can clear grass completely");
        stroke.Mode = "RestoreGrass";
        check(MapTerrainPainting.GrassDensity(0, 24, 1, stroke) == 24, "Restore preserves native densities above the authoring cap");
        stroke.Mode = "Texture";
        float[] Replay(MapTerrainRecipe recipe)
        {
            var values = (float[])original.Clone();
            foreach (var item in recipe.Strokes)
            foreach (var p in item.Points)
                MapTerrainPainting.Blend(
                    values,
                    original,
                    item.Layer,
                    MapTerrainPainting.Weight(25, 10, p, item),
                    item.Mode == "RestoreTexture"
                );
            return values;
        }
        check(
            Replay(layout.Terrain[0]).SequenceEqual(Replay(SeasonCompiler.Copy(layout.Terrain[0]))),
            "Saved stamps replay deterministically through the actual document serializer"
        );
        foreach (var invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -.1f, 21f })
        {
            var bad = Recipe();
            bad.Strokes[0].Radius = invalid;
            check(MapTerrainPainting.Errors(new() { bad }).Count > 0, "Rejects malformed brush radius " + invalid);
        }
        foreach (
            var action in new Action<MapTerrainRecipe>[]
            {
                r => r.Target = null!,
                r => r.Strokes = null!,
                r => r.Strokes[0] = null!,
                r => r.Strokes[0].Points = null!,
                r => r.Strokes[0].Points[0].X = 101,
                r => r.Strokes[0].Points[0].Z = -1,
                r => r.Strokes[0].Points[0].Y = float.NaN,
                r => r.Strokes[0].Layer = 3,
                r => r.Strokes[0].Mode = "Sculpt",
                r => r.Strokes[0].Density = 17,
                r => r.Target.Fingerprint = new string('x', 64),
                r => r.Strokes[0].Strength = 0,
                r => r.Strokes[0].Falloff = 2,
            }
        )
        {
            var bad = Recipe();
            action(bad);
            check(
                MapTerrainPainting.Errors(new() { bad }).Count > 0,
                "Malformed terrain recipes fail closed without dereferencing invalid fields"
            );
        }
        var over = Recipe();
        over.Strokes[0].Points = Enumerable.Repeat(new SpatialVector(), MapTerrainPainting.MaxPoints + 1).ToList();
        check(MapTerrainPainting.Errors(new() { over }).Count > 0, "Oversized stamp collections are rejected");
        var changed = Recipe();
        changed.Target.Fingerprint = new string('B', 64);
        check(MapTerrainPainting.Errors(new() { Recipe(), changed }).Count > 0, "Conflicting tile fingerprints cannot compose");

        var densityMap = new int[4, 4];
        MapTerrainPainting.CopyDensityCell(new[] { 1, 2, 3, 4 }, 2, 1, 0, densityMap);
        check(
            densityMap[0, 2] == 1 && densityMap[0, 3] == 2 && densityMap[1, 2] == 3 && densityMap[1, 3] == 4 && densityMap[2, 0] == 0,
            "Native density cells retain X/Z orientation and untouched neighbours"
        );
        var reduced = new int[2, 2];
        MapTerrainPainting.CopyDensityCell(new[] { 9 }, 1, 1, 1, reduced);
        check(reduced[1, 1] == 9 && reduced[0, 0] == 0, "Reduced-resolution grass prototypes retain their own sampling grid");
        var invalidCell = false;
        try
        {
            MapTerrainPainting.CopyDensityCell(new[] { 1, 2, 3 }, 2, 0, 0, densityMap);
        }
        catch (InvalidOperationException)
        {
            invalidCell = true;
        }
        check(invalidCell, "Mismatched native density cell sizes fail before copying");

        var session = new RaidEditorSession("woods")
        {
            Definition = new() { MapLayouts = new() { MapEditorChecks.Example() } },
            Hold = true,
        };
        session.Baseline = SeasonCompiler.Copy(session.Definition);
        session.Edit(d => d.MapLayouts[0].Terrain = new() { Recipe() });
        check(session.Definition!.FormatVersion == 14 && session.CanUndo, "A terrain stroke uses the real editor edit transaction");
        session.Undo(false);
        check(session.Definition!.MapLayouts[0].Terrain == null, "Undo removes the complete terrain stroke");
        session.Undo(true);
        check(session.Definition!.MapLayouts[0].Terrain![0].Strokes.Count == 1, "Redo restores exactly one stroke");
        var before = JsonConvert.SerializeObject(session.Definition);
        var rejected = false;
        try
        {
            session.Edit(d => d.MapLayouts[0].Terrain![0].Strokes[0].Radius = float.NaN);
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }
        check(
            rejected && JsonConvert.SerializeObject(session.Definition) == before,
            "Invalid paint leaves the committed document unchanged"
        );
        rejected = false;
        try
        {
            session.Edit(d => d.Name = new string('x', 4 * 1024 * 1024));
        }
        catch (InvalidOperationException e)
        {
            rejected = e.Message.Contains("message limit");
        }
        check(
            rejected && JsonConvert.SerializeObject(session.Definition) == before,
            "Actual serialized message size is preflighted and rolls back oversized edits"
        );

        rejected = false;
        try
        {
            session.Edit(d => d.Name = new string('"', 600000));
        }
        catch (InvalidOperationException e)
        {
            rejected = e.Message.Contains("message limit");
        }
        check(
            rejected && JsonConvert.SerializeObject(session.Definition) == before,
            "Escaped conflict responses are preflighted as complete transport messages"
        );

        MapLayout Layer(char id, string mode) =>
            new()
            {
                Id = new string(id, 24),
                Name = "Terrain " + id,
                Location = "woods",
                ApplyInNormalRaids = true,
                Terrain = new()
                {
                    new()
                    {
                        Target = Recipe().Target,
                        Strokes = new()
                        {
                            new()
                            {
                                Mode = mode,
                                Points = new() { new() },
                            },
                        },
                    },
                },
            };
        var first = Layer('a', "AddGrass");
        var second = Layer('b', "ClearGrass");
        var combined = MapLayerRules.Compose(new[] { first, second }, "woods")!;
        check(
            combined.Terrain!.SelectMany(r => r.Strokes).Select(s => s.Mode).SequenceEqual(new[] { "AddGrass", "ClearGrass" }),
            "Normal raid composition preserves ordered terrain operations"
        );
        second.ApplyInNormalRaids = false;
        check(MapLayerRules.Compose(new[] { first, second }, "woods")!.Terrain!.Count == 1, "Disabled map layers contribute no paint");
    }
}
