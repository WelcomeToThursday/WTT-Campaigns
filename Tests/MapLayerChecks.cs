using Newtonsoft.Json;
using WTT.Campaigns.Shared.Seasons;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Tests;

internal static class MapLayerChecks
{
    internal static void Run(Action<bool, string> check)
    {
        static string Id(int n) => n.ToString("x24");
        static MapLayout Layer(int n, string map = "woods") =>
            new()
            {
                Id = Id(n),
                Name = "Layer " + n,
                Location = map,
                ApplyInNormalRaids = true,
            };
        static MapObjectEdit Edit(int n, string path) =>
            new()
            {
                Id = Id(n),
                Name = "Scenery",
                Location = "woods",
                Scene = "woods",
                Target = new()
                {
                    Scene = "woods",
                    Path = path,
                    Fingerprint = new string('a', 64),
                },
            };
        var old = JsonConvert.DeserializeObject<MapLayout>("{\"Id\":\"000000000000000000000001\"}")!;
        check(!old.ApplyInNormalRaids, "Existing layouts default to no ordinary-raid changes");
        check(!JsonConvert.SerializeObject(old).Contains("ApplyInNormalRaids"), "Disabled layers preserve existing gameplay serialization");
        var first = Layer(1);
        var second = Layer(2);
        var disabled = Layer(3);
        disabled.ApplyInNormalRaids = false;
        disabled.Objects.Add(Edit(99, "Scenery[0]/A[0]"));
        first.Objects.Add(Edit(10, "Scenery[0]/A[0]"));
        second.Objects.Add(Edit(20, "Scenery[0]/B[1]"));
        first.Start = new() { Id = Id(11) };
        first.Checkpoints.Add(new() { Id = Id(12) });
        first.Exit = new() { Id = Id(13) };
        first.Encounters.Add(new());
        var layers = new[] { first, second, disabled, Layer(4, "factory4_day") };
        var merged = MapLayerRules.Compose(layers, "woods")!;
        var published = new[]
        {
            new SeasonDefinition
            {
                Id = Id(100),
                MapLayouts = new() { first },
            },
            new SeasonDefinition
            {
                Id = Id(101),
                MapLayouts = new() { second },
            },
        };
        check(
            MapLayerRules.ForCharacter(published, "", "woods")!.Objects.Count == 2,
            "Regular characters combine enabled layers across published campaigns"
        );
        check(
            MapLayerRules.ForCharacter(published, Id(100), "woods")!.Objects.Single().Id == Id(10),
            "Campaign characters use only their own campaign layers"
        );
        check(
            MapLayerRules.ForCharacter(published, Id(102), "woods") == null,
            "An unavailable campaign never falls back to another campaign's layers"
        );
        var preferences = new MapLayerPreferences();
        preferences.Overrides[MapLayerRules.Key(Id(100), first.Id)] = false;
        var roundTrip = SeasonCompiler.Copy(preferences);
        check(
            MapLayerRules.ForCharacter(published, "", "woods", roundTrip.Overrides)!.Objects.Single().Id == Id(20),
            "A saved regular-character override disables its exact campaign and layout"
        );
        check(
            MapLayerRules.ForCharacter(published, Id(100), "woods", roundTrip.Overrides)!.Objects.Single().Id == Id(10),
            "Regular-character selections never change campaign-character layers"
        );
        second.ApplyInNormalRaids = false;
        preferences.Overrides[MapLayerRules.Key(Id(101), second.Id)] = true;
        check(
            MapLayerRules.ForCharacter(published, "", "woods", preferences.Overrides)!.Objects.Single().Id == Id(20),
            "The menu can enable a published layer whose authored default is disabled"
        );
        check(!second.ApplyInNormalRaids && first.ApplyInNormalRaids, "Menu overrides do not mutate published layer defaults");
        second.ApplyInNormalRaids = true;
        check(merged.Objects.Count == 2, "Only enabled layers for the exact map are combined");
        check(
            merged.Start == null && merged.Exit == null && merged.Checkpoints.Count == 0 && merged.Encounters.Count == 0,
            "Ordinary layers do not activate mission routes or AI"
        );
        check(MapLayoutRules.Errors(merged).Count == 0, "Ordinary map layers do not require a player route");
        check(MapLayerRules.Compose(layers, "Woods") == null, "Map layer matching is exact");
        check(SeasonCompiler.Copy(first).ApplyInNormalRaids, "Layer enablement survives draft round trips");
        var before = JsonConvert.SerializeObject(layers);
        MapLayerRules.Compose(layers, "woods");
        check(before == JsonConvert.SerializeObject(layers), "Layer composition preserves authored layouts");
        second.Objects[0].Target.Path = first.Objects[0].Target.Path;
        check(MapLayerRules.Errors(layers).Any(e => e.Contains("conflicting overrides")), "Conflicting enabled layers are rejected");
        second.Objects[0].Target.Path = first.Objects[0].Target.Path + "/Child[0]";
        check(
            MapLayerRules.Errors(layers).Any(e => e.Contains("parent or its children")),
            "Parent-child overrides across layers are rejected"
        );
        second.ApplyInNormalRaids = false;
        check(!MapLayerRules.Errors(layers).Any(), "Disabling a conflicting layer resolves the conflict");
        var season = new SeasonDefinition();
        season.MapLayouts.Add(first);
        var hash = WTT.Campaigns.Server.Seasons.SeasonRepository.GameplayHash(season);
        first.ApplyInNormalRaids = false;
        check(
            hash != WTT.Campaigns.Server.Seasons.SeasonRepository.GameplayHash(season),
            "Changing enabled layers changes campaign gameplay identity"
        );
    }
}
