using WTT.Campaigns.Client.Encounters;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Tests;

internal static class EncounterCoreChecks
{
    internal static void Run()
    {
        Check(!EncounterMovementPolicy.ReturnToAnchor(false, 8.9f), "Idle bots hold within the outer boundary");
        Check(EncounterMovementPolicy.ReturnToAnchor(false, 9.1f), "Displaced idle bots return");
        Check(EncounterMovementPolicy.ReturnToAnchor(true, 4f), "Returning bots avoid oscillating at the outer boundary");
        Check(!EncounterMovementPolicy.ReturnToAnchor(true, 2.25f), "Returning bots stop within the inner boundary");
        Check(EncounterMovementPolicy.Speed(false) < EncounterMovementPolicy.Speed(true), "Walk is slower than run");
        Check(EncounterMovementPolicy.KeepPath(.5f), "A normal refresh must preserve native corner progress");
        Check(EncounterMovementPolicy.KeepPath(2.99f), "Slow but progressing walking paths are retained");
        Check(!EncounterMovementPolicy.KeepPath(3f), "A stalled path is eligible for a bounded retry");
        var a = new SpatialCapture { Id = "a" };
        var b = new SpatialCapture { Id = "b" };
        var c = new SpatialCapture { Id = "c" };
        var points = new[] { c, b, a };
        Check(EncounterCorePlan.Anchors(points, _ => true, (_, _) => false).Count == 0, "Connected native islands need no added cores");
        Check(
            EncounterCorePlan.Anchors(points, _ => false, (_, _) => true).Single().Id == "a",
            "A shared island gets one deterministic anchor"
        );
        Check(
            EncounterCorePlan.Anchors(points, p => p.Id == "c", (x, y) => x.Id == y.Id).Count == 2,
            "Separated islands get separate anchors"
        );
        Check(
            EncounterCorePlan.Anchors(new[] { a, b }, _ => false, (x, _) => x.Id == "b").Count == 2,
            "One-way paths cannot merge core groups"
        );
        Check(
            EncounterCorePlan.Anchors(Array.Empty<SpatialCapture>(), _ => false, (_, _) => true).Count == 0,
            "Empty layouts create no cores"
        );
        var used = new HashSet<int> { 0, 1, 3, int.MaxValue };
        Check(
            EncounterCorePlan.Allocate(used) == 2 && EncounterCorePlan.Allocate(used) == 4,
            "Core identities skip existing and newly reserved IDs without overflow"
        );
        var layout = new MapLayout
        {
            SpawnPoints = new() { a, b, c },
            Encounters = new()
            {
                new MapEncounter
                {
                    Waves = new()
                    {
                        new MapEncounterWave
                        {
                            Roster = new()
                            {
                                new MapEncounterRosterEntry
                                {
                                    SpawnPointIds = new() { "a", "a", "b" },
                                },
                            },
                        },
                    },
                },
            },
        };
        Check(
            EncounterCorePlan.Assigned(layout).Select(p => p.Id).SequenceEqual(new[] { "a", "b" }),
            "Only assigned spawns contribute anchors, without duplicate reservations"
        );
    }

    private static void Check(bool passed, string message)
    {
        if (!passed)
            throw new InvalidOperationException(message);
    }
}
