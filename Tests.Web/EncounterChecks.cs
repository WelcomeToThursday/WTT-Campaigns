using Newtonsoft.Json;
using WTT.Campaigns.Server.Editor;
using WTT.Campaigns.Shared.Authoring;
using WTT.Campaigns.Shared.Native;
using WTT.Campaigns.Shared.Seasons;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Web.Tests;

internal static class EncounterChecks
{
    private static string Id() => Guid.NewGuid().ToString("N")[..24];

    internal static void Run(Action<bool, string> check)
    {
        var spawn = Point("Spawn", 1);
        var waypointA = Point("Patrol A", 2);
        var waypointB = Point("Patrol B", 3);
        var trigger = new MapVolume
        {
            Id = Id(),
            Name = "Trigger",
            Location = "woods",
            Scene = "woods_main",
            Position = new SpatialVector
            {
                X = 4,
                Y = 1,
                Z = 2,
            },
        };
        var route = new MapPatrolRoute
        {
            Id = Id(),
            Waypoints = new() { waypointA, waypointB },
        };
        var roster = new MapEncounterRosterEntry
        {
            Id = Id(),
            SpawnPointIds = new() { spawn.Id },
            PatrolRouteId = route.Id,
            SquadId = Id(),
        };
        var layout = new MapLayout
        {
            Id = Id(),
            Name = "Encounter layout",
            Location = "woods",
            SpawnPoints = new() { spawn },
            PatrolRoutes = new() { route },
            Encounters = new()
            {
                new MapEncounter
                {
                    Id = Id(),
                    Trigger = new MapEncounterTrigger { Volume = trigger },
                    Waves = new()
                    {
                        new MapEncounterWave
                        {
                            Id = Id(),
                            Roster = new() { roster },
                        },
                    },
                },
            },
        };

        var roundTrip = JsonConvert.DeserializeObject<MapLayout>(JsonConvert.SerializeObject(layout))!;
        check(roundTrip.Encounters.Count == 1, "Encounter records survive JSON roundtrip");
        check(roundTrip.Encounters[0].Waves[0].Roster[0].PatrolRouteId == route.Id, "Roster route references survive JSON roundtrip");
        check(MapLayoutRules.Format(new[] { layout }) == 6, "AI layout records require format 6");
        check(MapEncounterRules.Errors(layout).Count == 0, "Complete encounter layout passes draft validation");
        var spatialOnly = new MapLayout
        {
            Id = Id(),
            Name = "Spatial-only layout",
            Location = layout.Location,
            SpawnPoints = new(),
            PatrolRoutes = new(),
            Encounters = new(),
        };
        check(MapEncounterRules.Errors(spatialOnly).Count == 0, "Layouts without encounters remain valid");

        var source = new List<NativeItem>
        {
            new() { Id = "equipment", Template = "equipment-tpl" },
            new()
            {
                Id = "pockets",
                Template = "pockets-tpl",
                ParentId = "equipment",
                SlotId = "Pockets",
            },
            new()
            {
                Id = "rig",
                Template = "rig-tpl",
                ParentId = "equipment",
                SlotId = "TacticalVest",
            },
            new()
            {
                Id = "magazine",
                Template = "magazine-tpl",
                ParentId = "rig",
                SlotId = "main",
            },
            new() { Id = "stash", Template = "stash-tpl" },
        };
        var before = JsonConvert.SerializeObject(source);
        var preview = EditorPreviewGearCopy.Copy(source, "equipment");
        check(JsonConvert.SerializeObject(source) == before, "Preview gear copy does not mutate the source profile");
        check(
            preview.Slots.Count == 2 && preview.Slots.Sum(s => s.Items.Count) == 3,
            "Preview copies equipped trees and excludes stash items"
        );
        check(
            preview.Slots.SelectMany(s => s.Items).All(item => item.Id != "equipment"),
            "Preview does not expose the source equipment root"
        );
        check(
            preview.Slots.Single(s => s.Slot == "TacticalVest").Items.Single(i => i.Template == "rig-tpl").ParentId == null,
            "Preview detaches each equipped slot root"
        );
        var rejectedMissingPockets = false;
        try
        {
            EditorPreviewGearCopy.Copy(source.Where(item => item.Id != "pockets"), "equipment");
        }
        catch (InvalidOperationException)
        {
            rejectedMissingPockets = true;
        }
        check(rejectedMissingPockets, "Preview gear rejects a selected character without pockets.");
    }

    private static SpatialCapture Point(string name, float x) =>
        new()
        {
            Id = Id(),
            Name = name,
            Location = "woods",
            Scene = "woods_main",
            Position = new SpatialVector
            {
                X = x,
                Y = 1,
                Z = 2,
            },
        };
}
