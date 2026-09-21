using System.Numerics;
using WTT.Campaigns.Client.Encounters;

namespace WTT.Campaigns.Tests;

internal static class EncounterClearanceChecks
{
    internal static void Run(Action<bool, string> check)
    {
        check(EncounterObstaclePolicy.WalkableContact(1, 0), "Flat floor contacts remain walkable");
        check(EncounterObstaclePolicy.WalkableContact(.8f, .1f), "Walkable slope contacts near the feet remain walkable");
        check(!EncounterObstaclePolicy.WalkableContact(1, .6f), "An upward-facing bollard top is an obstruction");
        check(!EncounterObstaclePolicy.WalkableContact(0, 0), "A vertical barrier remains an obstruction at ground height");
        var bollard = new EncounterRouteClearance.Obstacle(Vector3.Zero, new(.2f, 1, .2f));
        var line = new[] { new Vector3(-4, 0, 0), new Vector3(4, 0, 0) };
        bool Clear(Vector3 from, Vector3 to) => !Intersects(from, to, bollard);
        var around = EncounterRouteClearance.Adjust(line, p => p, Clear, (_, _) => bollard);
        check(around.Length == 4 && AllClear(around, Clear), "A straight route detours around a bollard with full padded clearance");
        check(around[0] == line[0] && around[^1] == line[^1], "Detours preserve exact authored endpoints");
        bool OneSide(Vector3 from, Vector3 to) => from.Z >= 0 && to.Z >= 0 && Clear(from, to);
        var oneSide = EncounterRouteClearance.Adjust(line, p => p.Z < 0 ? null : p, OneSide, (_, _) => bollard);
        check(oneSide.Length == 4 && AllClear(oneSide, OneSide), "A barrier on one side selects the opposite valid detour");
        var noSpace = EncounterRouteClearance.Adjust(line, _ => null, Clear, (_, _) => bollard);
        check(
            noSpace.SequenceEqual(line) && !AllClear(noSpace, Clear),
            "No valid detour preserves the blocked result for strict final validation"
        );
        var reversed = EncounterRouteClearance.Adjust(line.Reverse().ToArray(), p => p, Clear, (_, _) => bollard);
        check(reversed.Length == 4 && AllClear(reversed, Clear), "The reverse direction receives an independently validated detour");
        var container = new EncounterRouteClearance.Obstacle(new(-2, 0, 2), new(1.5f, 1, 1.5f));
        var bend = new[] { new Vector3(-5, 0, 0), Vector3.Zero, new Vector3(0, 0, 6) };
        bool CornerClear(Vector3 from, Vector3 to) => !Intersects(from, to, container);
        var rounded = EncounterRouteClearance.Adjust(bend, p => p, CornerClear, (_, _) => container);
        check(
            rounded.Length >= 3 && rounded[1] != bend[1] && AllClear(rounded, CornerClear),
            "A container bend shifts away from the padded footprint without changing endpoints"
        );
        var unchanged = EncounterRouteClearance.Adjust(
            bend,
            _ => throw new Exception("unnecessary sample"),
            (_, _) => true,
            (_, _) => null
        );
        check(unchanged.SequenceEqual(bend), "Already spacious routes retain all calculated corners");
        var samples = 0;
        var longRoute = Enumerable.Range(0, 100).Select(i => new Vector3(i, 0, 0)).ToArray();
        EncounterRouteClearance.Adjust(
            longRoute,
            p =>
            {
                samples++;
                return null;
            },
            (_, _) => false,
            (_, _) => bollard
        );
        check(samples <= EncounterRouteClearance.CandidateLimit * 2, "Long broken routes cannot exceed the bounded candidate budget");

        var east = Vector3.UnitX;
        var west = -east;
        float Pace(float x, bool held = false) => EncounterSquadSpacing.Pace(Vector3.Zero, east, new(x, 0, 0), east, true, held);
        check(
            Pace(1.8f) == 0 && Pace(2.5f, true) == 0 && Pace(2.8f, true) > 0,
            "Following gap uses separate stop and resume distances to prevent shuffling"
        );
        check(Pace(3) < 1 && Pace(4) == 1, "Followers slow before reaching the stopping gap and recover full pace when clear");
        check(Pace(-1) == 1, "A bot does not stop for a squadmate behind it");
        check(
            EncounterSquadSpacing.Pace(Vector3.Zero, east, new(1, 2, 0), east, true, false) == 1,
            "Bots on different floors do not hold each other"
        );
        check(
            EncounterSquadSpacing.Pace(Vector3.Zero, east, new(1, 0, 2), east, true, false) == 1,
            "Side-by-side traffic outside the movement corridor remains free"
        );
        var lowPriority = EncounterSquadSpacing.Pace(Vector3.Zero, east, new(1, 0, 0), west, true, false);
        var highPriority = EncounterSquadSpacing.Pace(new(1, 0, 0), west, Vector3.Zero, east, false, false);
        check(lowPriority == 0 && highPriority == 1, "Opposing traffic chooses one yielding bot rather than a mutual stop");
        check(
            EncounterSquadSpacing.Pace(Vector3.Zero, east, Vector3.Zero, east, true, false) == 0
                && EncounterSquadSpacing.Pace(Vector3.Zero, east, Vector3.Zero, east, false, false) == 1,
            "Overlapping squad members separate using stable priority"
        );
    }

    private static bool AllClear(Vector3[] path, Func<Vector3, Vector3, bool> clear) =>
        Enumerable.Range(1, path.Length - 1).All(i => clear(path[i - 1], path[i]));

    private static bool Intersects(Vector3 from, Vector3 to, EncounterRouteClearance.Obstacle obstacle)
    {
        var half = obstacle.Extents + new Vector3(EncounterRouteClearance.PreferredRadius);
        var a = from - obstacle.Center;
        var delta = to - from;
        var minimum = 0f;
        var maximum = 1f;
        foreach (var (start, direction, extent) in new[] { (a.X, delta.X, half.X), (a.Z, delta.Z, half.Z) })
        {
            if (MathF.Abs(direction) < .00001f)
            {
                if (MathF.Abs(start) > extent)
                    return false;
                continue;
            }
            var first = (-extent - start) / direction;
            var last = (extent - start) / direction;
            minimum = MathF.Max(minimum, MathF.Min(first, last));
            maximum = MathF.Min(maximum, MathF.Max(first, last));
            if (minimum > maximum)
                return false;
        }
        return true;
    }
}
