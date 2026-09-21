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
        Check(EncounterMovementPolicy.AtWaypoint(.25f), "Arrival is detected before native final stopping distance");
        Check(!EncounterMovementPolicy.AtWaypoint(.37f), "Arrival cannot cut a waypoint outside its radius");
        Check(EncounterMovementPolicy.PatrolReachDistance < .5f, "Tight patrol corners avoid native half-meter corner cutting");
        var progress = new EncounterPathProgress();
        progress.Reset(10, 0);
        Check(
            progress.Observe(9.9f, 1) && progress.Observe(10.2f, 2) && !progress.Observe(9.9f, 3),
            "Back-and-forth shuffling does not reset the stall timer"
        );
        progress.Reset(10, 0);
        Check(
            progress.Observe(9.8f, 2.5f) && progress.Observe(9.6f, 5) && progress.Observe(9.4f, 7.5f),
            "Slow forward progress along the path preserves the native cursor"
        );
        Check(!progress.Observe(float.PositiveInfinity, 8), "An exhausted native cursor requests a new path");
        progress.Reset(4, 20);
        Check(progress.Observe(4, 22.9f) && !progress.Observe(4, 23), "Submitting a recovery path starts one new retry interval");
        bool Same(float[] old, int index, float[] fresh) =>
            EncounterMovementPolicy.SameRemainingCorners(
                old.Length,
                index,
                fresh.Length,
                (a, b) => (old[a] - fresh[b]) * (old[a] - fresh[b])
            );
        Check(Same([0, 5, 10], 1, [2, 5, 10]), "A shifted origin with identical future corners preserves the native cursor");
        Check(Same([0, 5, 10], 0, [0, 5, 10]), "An unchanged path still on its origin is retained");
        Check(Same([0, 5, 10], 2, [6, 10]), "Already consumed corners do not force path replacement");
        Check(!Same([0, 5, 10], 1, [2, 6, 10]), "A changed bend requires the newly validated path");
        Check(!Same([0, 5, 10], 1, [2, 10]), "A newly simplified path is submitted without reusing the old corner sequence");
        Check(!Same([0, 5, 10], 2, [4, 5, 10]), "Early corner cutting restores a missing required corner");
        Check(!Same([0, 5, 10], 3, [10, 10]) && !Same([0, 5, 10], -1, [0, 10]), "Invalid native cursors are never retained");
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
