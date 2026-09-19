using Newtonsoft.Json;
using WTT.Campaigns.Shared.Seasons;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Tests;

internal static class HazardChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var oldZone = JsonConvert.DeserializeObject<SeasonZone>("{\"Shape\":\"Box\"}")!;
        check(oldZone.Hazard == null && oldZone.Uses.Contains("VisitPlace"), "Legacy zones remain ordinary quest zones");
        var oldSniper = JsonConvert.DeserializeObject<HazardSettings>("{\"Kind\":\"Sniper\"}")!;
        check(oldSniper.PlayShotSound && !oldSniper.SuppressedShots, "Existing sniper zones default to audible normal shots");
        foreach (var (play, suppressed) in new[] { (true, false), (true, true), (false, false), (false, true) })
        {
            var settings = new HazardSettings
            {
                Kind = "Sniper",
                PlayShotSound = play,
                SuppressedShots = suppressed,
            };
            var copy = SeasonCompiler.Copy(settings);
            check(
                copy.PlayShotSound == play && copy.SuppressedShots == suppressed,
                "Normal, suppressed and silent settings survive serialization"
            );
        }
        check(
            new[] { "SniperPlaySound", "SniperSuppressed" }.All(id => EditorToolkitChecks.Nodes().Any(n => n.Id == id)),
            "Sniper inspector exposes sound and suppression controls"
        );
        var source = new MapLayout
        {
            Id = "111111111111111111111111",
            Name = "Source",
            Location = "woods",
        };
        var target = new MapLayout
        {
            Id = "222222222222222222222222",
            Name = "Target",
            Location = "woods",
        };
        var season = new SeasonDefinition
        {
            FormatVersion = 9,
            MapLayouts = new() { source, target },
        };
        foreach (var kind in HazardRules.Kinds)
        {
            var zone = new SeasonZone
            {
                Id = Guid.NewGuid().ToString("N")[..24],
                Name = kind,
                Location = "woods",
                Scene = "woods_main",
                LayoutId = source.Id,
                Uses = new(),
                Hazard = new() { Kind = kind },
            };
            season.Zones.Add(zone);
            check(SpatialRules.Errors(season).Count == 0, kind + " is accepted as a dedicated hazard volume");
            var roundtrip = SeasonCompiler.Copy(zone);
            check(
                roundtrip.Hazard?.Kind == kind && roundtrip.Uses.Count == 0 && roundtrip.LayoutId == source.Id,
                kind + " survives draft and mission serialization without becoming a quest trigger"
            );
            roundtrip.Shape = "Sphere";
            check(HazardRules.Errors(roundtrip).Any(), "Native border hazards reject unsupported sphere geometry");
            roundtrip.Shape = "Box";
            roundtrip.Uses.Add("VisitPlace");
            check(HazardRules.Errors(roundtrip).Any(), "Hazard and quest trigger roles cannot be mixed");
            roundtrip.Uses.Clear();
            roundtrip.Size.X = float.NaN;
            check(HazardRules.Errors(roundtrip).Any(), "Hazards reject nonfinite bounds before runtime creation");
        }
        var copies = ZoneLayoutRules.CopyOwnedZones(season, source.Id, target.Id);
        check(
            copies.Count == 4
                && copies.All(c => c.LayoutId == target.Id && c.Hazard != null)
                && !copies.Any(c => season.Zones.Any(z => z.Id == c.Id)),
            "Layout duplication preserves all hazard kinds with fresh IDs"
        );
        copies[0].Hazard!.Kind = "Invalid";
        check(season.Zones[0].Hazard!.Kind == "Minefield", "Copied hazard settings are detached from their source");
        check(HazardRules.Errors(copies[0]).Any(), "Unknown hazard kinds are rejected");
        check(!ZoneLayoutRules.ForEditor(season.Zones, target.Id).Any(), "Other layouts do not inherit owned hazards");
        season.Zones[0].LayoutId = "";
        check(
            ZoneLayoutRules.ForEditor(season.Zones, target.Id).Single().Hazard!.Kind == "Minefield",
            "Shared hazards remain visible across layouts"
        );
        check(
            EditorToolkitChecks.Nodes().Any(n => n.Id == "Hazards" && n.Kind == "button")
                && HazardRules.Kinds.All(k => EditorToolkitChecks.Nodes().Any(n => n.Id == "Add" + k)),
            "Hazard tool exposes all four creation actions"
        );
    }
}
