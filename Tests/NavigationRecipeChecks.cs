using Newtonsoft.Json;
using WTT.Campaigns.Client.Authoring;
using WTT.Campaigns.Shared.Seasons;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Tests;

internal static class NavigationRecipeChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var legacy = MapEditorChecks.Example();
        check(
            legacy.Navigation == null && !JsonConvert.SerializeObject(legacy).Contains("\"Navigation\""),
            "Legacy layouts keep native navigation and omit recipes"
        );
        var recipe = new MapNavigationRecipe
        {
            TestLocation = new()
            {
                X = -30,
                Y = 21,
                Z = -330,
            },
            Settings = new(),
        };
        legacy.Navigation = recipe;
        var copy = SeasonCompiler.Copy(legacy);
        check(
            copy.Navigation?.TestLocation?.X == -30 && copy.Navigation.Settings?.TileSize == 256,
            "Layout navigation recipe round-trips through the actual document serializer"
        );
        copy.Navigation!.Settings!.Radius = 1;
        check(recipe.Settings!.Radius == .5f, "Recipe copies do not share mutable settings");
        check(
            MapNavigationRules.Errors(new()).Count == 0 && MapNavigationRules.Errors(recipe).Count == 0,
            "Native and valid custom recipes pass"
        );
        foreach (var invalid in new[] { float.NaN, float.NegativeInfinity, float.PositiveInfinity, -1f, 100f })
        {
            foreach (var field in new[] { "Radius", "Height", "Slope", "Step", "VoxelSize" })
            {
                var bad = SeasonCompiler.Copy(recipe);
                typeof(MapNavigationSettings).GetProperty(field)!.SetValue(bad.Settings, invalid);
                check(MapNavigationRules.Errors(bad).Count > 0, "Navigation rejects invalid " + field + ": " + invalid);
            }
        }
        foreach (var tile in new[] { 0, 15, 17, 1025, int.MaxValue })
        {
            var bad = SeasonCompiler.Copy(recipe);
            bad.Settings!.TileSize = tile;
            check(MapNavigationRules.Errors(bad).Count > 0, "Navigation rejects unsupported tile size " + tile);
        }
        foreach (var version in new[] { 0, 3, 99 })
            check(MapNavigationRules.Errors(new() { Version = version }).Count > 0, "Unknown recipe versions fail closed");
        check(
            MapNavigationRules.Errors(new() { TestLocation = new() { X = float.NaN } }).Count > 0,
            "Recipe rejects nonfinite test locations"
        );
        check(
            MapNavigationRules
                .Errors(
                    new()
                    {
                        Settings = new() { Height = .5f, Step = .6f },
                    }
                )
                .Count > 0,
            "Step cannot exceed standing height"
        );
        check(MapLayoutRules.Format(new[] { legacy }) == 13, "Saved navigation requires format 13");
        var session = new RaidEditorSession("woods")
        {
            Definition = new() { MapLayouts = new() { MapEditorChecks.Example() } },
            Hold = true,
        };
        session.Baseline = SeasonCompiler.Copy(session.Definition);
        session.Edit(d => d.MapLayouts[0].Navigation = recipe);
        check(
            session.Definition!.FormatVersion == 13 && session.Definition.MapLayouts[0].Navigation != null,
            "Actual editor recipe edits promote the document format"
        );
        session.Undo(false);
        check(session.Definition!.MapLayouts[0].Navigation == null, "Undo restores native layout behavior");
        session.Undo(true);
        check(session.Definition!.MapLayouts[0].Navigation?.Settings?.Radius == .5f, "Redo restores the recipe");

        MapLayout Layer(string id) =>
            new()
            {
                Id = id,
                Location = "woods",
                ApplyInNormalRaids = true,
                Name = "Layer",
            };
        var first = Layer(new string('a', 24));
        var second = Layer(new string('b', 24));
        first.Navigation = recipe;
        var composed = MapLayerRules.Compose(new[] { first, second }, "woods")!;
        check(
            composed.Navigation?.TestLocation?.Y == 21 && !ReferenceEquals(composed.Navigation, recipe),
            "Composition retains one independent recipe for the combined map"
        );
        second.Navigation = new();
        var rejected = false;
        try
        {
            MapLayerRules.Compose(new[] { first, second }, "woods");
        }
        catch (InvalidOperationException error)
        {
            rejected = error.Message.Contains("more than one navigation recipe");
        }
        check(rejected, "Composition rejects multiple navigation recipes");
        second.ApplyInNormalRaids = false;
        check(
            MapLayerRules.Compose(new[] { first, second }, "woods")!.Navigation != null,
            "Disabled layers do not conflict with the chosen recipe"
        );
    }
}
