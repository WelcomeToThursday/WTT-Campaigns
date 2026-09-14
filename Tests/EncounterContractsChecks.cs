using Newtonsoft.Json;
using WTT.Campaigns.Shared.Seasons;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Tests;

internal static class EncounterContractsChecks
{
    private static string Id() => Guid.NewGuid().ToString("N")[..24];

    private static SpatialCapture Point(string name, float x = 0) =>
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

    internal static void Run(Action<bool, string> check)
    {
        var spawn = Point("Spawn", 1);
        var waypointA = Point("Patrol A", 2);
        var waypointB = Point("Patrol B", 3);
        var triggerVolume = new MapVolume
        {
            Id = Id(),
            Name = "AI trigger",
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
        var rosterId = Id();
        var wave = new MapEncounterWave
        {
            Id = Id(),
            Roster = new()
            {
                new MapEncounterRosterEntry
                {
                    Id = rosterId,
                    SpawnPointIds = new() { spawn.Id },
                    PatrolRouteId = route.Id,
                    SquadId = Id(),
                },
            },
        };
        var encounter = new MapEncounter
        {
            Id = Id(),
            Trigger = new MapEncounterTrigger { Volume = triggerVolume },
            Waves = new() { wave },
        };
        var layout = new MapLayout
        {
            Id = Id(),
            Name = "AI layout",
            Location = "woods",
            SpawnPoints = new() { spawn },
            PatrolRoutes = new() { route },
            Encounters = new() { encounter },
        };

        var roundTrip = JsonConvert.DeserializeObject<MapLayout>(JsonConvert.SerializeObject(layout))!;
        check(
            roundTrip.Encounters[0].Waves[0].Roster[0].SpawnPointIds.Single() == spawn.Id,
            "Encounter spawn references survive JSON roundtrip"
        );
        check(
            roundTrip.PatrolRoutes[0].Pace == MapPatrolRoute.Walk && roundTrip.PatrolRoutes[0].Completion == MapPatrolRoute.Loop,
            "Patrol defaults are walk and loop"
        );
        check(roundTrip.PatrolRoutes[0].WaitSeconds.Count == 0, "Patrol defaults omit zero waits");
        check(MapLayoutRules.Format(new[] { layout }) == 6, "AI layout records require campaign format 6");
        check(MapLayoutRules.NeedsFormat6(layout), "AI layout format detection includes encounters");
        check(
            MapLayoutRules
                .Points(layout)
                .Select(p => p.Id)
                .ToHashSet()
                .SetEquals(new[] { spawn.Id, waypointA.Id, waypointB.Id, triggerVolume.Id }),
            "AI spawn, patrol and trigger points participate in layout point enumeration"
        );
        var owned = MapLayoutRules.OwnedIds(layout).ToHashSet();
        check(
            new[] { layout.Id, spawn.Id, waypointA.Id, waypointB.Id, triggerVolume.Id, route.Id, encounter.Id, wave.Id, rosterId }.All(
                owned.Contains
            ),
            "AI route, trigger, encounter, wave and roster identities are owned by the layout"
        );

        var navigation = new TestNavigation();
        check(
            MapEncounterRules.Errors(layout, navigation, true, true).Count == 0,
            "Complete AI layout passes strict structural and navigation validation"
        );
        navigation.OffMeshX = waypointB.Position.X;
        check(
            MapEncounterRules.Errors(layout, navigation, true, true).Any(e => e.Contains("off the NavMesh")),
            "Off-NavMesh AI points are rejected before preview"
        );
        navigation.OffMeshX = null;
        navigation.Unreachable = true;
        check(
            MapEncounterRules.Errors(layout, navigation, true, true).Any(e => e.Contains("loop closure")),
            "Loop closure must be reachable"
        );

        var draft = new MapLayout
        {
            Id = Id(),
            Name = "Incomplete AI draft",
            Location = "woods",
            PatrolRoutes = new()
            {
                new MapPatrolRoute
                {
                    Id = Id(),
                    Waypoints = new() { Point("Only point") },
                },
            },
            Encounters = new() { new MapEncounter { Id = Id() } },
        };
        check(MapEncounterRules.Errors(draft).Count == 0, "Incomplete AI records remain editable as drafts");
        check(MapEncounterRules.Errors(draft, navigation, true, true).Count > 0, "Incomplete AI records cannot run in preview");

        var editRoute = new MapPatrolRoute
        {
            Id = Id(),
            Waypoints = new() { Point("A"), Point("B"), Point("C") },
            WaitSeconds = new() { 1, 2, 3 },
        };
        check(
            MapPatrolRouteEditing.RemoveAt(editRoute, 0) && editRoute.WaitSeconds.SequenceEqual(new[] { 2f, 3f }),
            "Removing the first waypoint removes its aligned wait"
        );
        check(
            MapPatrolRouteEditing.RemoveAt(editRoute, 1) && editRoute.WaitSeconds.SequenceEqual(new[] { 2f }),
            "Removing a middle waypoint removes its aligned wait"
        );
        MapPatrolRouteEditing.Append(editRoute, Point("D"));
        check(
            editRoute.WaitSeconds.SequenceEqual(new[] { 2f, 0f }),
            "Appending a waypoint extends an authored wait list at the same index"
        );
        var zeroWaitRoute = new MapPatrolRoute
        {
            Id = Id(),
            Waypoints = new() { Point("A") },
        };
        MapPatrolRouteEditing.Append(zeroWaitRoute, Point("B"));
        check(zeroWaitRoute.WaitSeconds.Count == 0, "Appending to a zero-wait route keeps canonical empty waits");
    }

    private sealed class TestNavigation : IEncounterNavigation
    {
        internal float? OffMeshX;
        internal bool Unreachable;

        public bool IsOnNavMesh(SpatialVector position) => position.Finite && position.X != OffMeshX;

        public bool HasStandingClearance(SpatialVector position) => position.Finite;

        public bool HasCompletePath(SpatialVector from, SpatialVector to) => !Unreachable;
    }
}
