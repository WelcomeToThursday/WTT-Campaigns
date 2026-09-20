using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Client.Encounters;

internal static class EncounterMovementPolicy
{
    internal const float PatrolReachDistance = .2f;

    internal static bool AtWaypoint(float squaredDistance) => squaredDistance <= .36f;

    internal static int Continuation(MapPatrolRoute route, int waypoint, int direction)
    {
        if (waypoint < 0 || waypoint >= route.Waypoints.Count || route.Waypoints.Count < 2)
            return -1;
        if (route.WaitSeconds.Count > waypoint && route.WaitSeconds[waypoint] != 0)
            return -1;
        return route.Completion switch
        {
            MapPatrolRoute.Loop => (waypoint + 1) % route.Waypoints.Count,
            MapPatrolRoute.Stop => waypoint + 1 < route.Waypoints.Count ? waypoint + 1 : -1,
            MapPatrolRoute.PingPong when direction is 1 or -1 =>
                waypoint + direction < 0 || waypoint + direction >= route.Waypoints.Count ? waypoint - direction : waypoint + direction,
            _ => -1,
        };
    }

    internal static T[] JoinLegs<T>(T[] approach, T[] continuation)
    {
        var result = new T[approach.Length + continuation.Length - 1];
        Array.Copy(approach, result, approach.Length);
        Array.Copy(continuation, 1, result, approach.Length, continuation.Length - 1);
        return result;
    }

    // Native completion measures straight-line distance to the final point,
    // even on a multi-corner path. Do not append an endpoint near an earlier
    // part of the approach (notably a ping-pong return to the departure point).
    internal static bool CanJoin(System.Numerics.Vector3[] approach, System.Numerics.Vector3 destination)
    {
        for (var i = 1; i < approach.Length; i++)
        {
            var segment = approach[i] - approach[i - 1];
            var length = segment.LengthSquared();
            var fraction = length > .0001f
                ? Math.Clamp(System.Numerics.Vector3.Dot(destination - approach[i - 1], segment) / length, 0, 1) : 0;
            if (System.Numerics.Vector3.DistanceSquared(destination, approach[i - 1] + segment * fraction) <= 4f)
                return false;
        }
        return approach.Length >= 2;
    }

    internal static bool NeedsSquadPlan(PatrolRuntimeStatus status, bool planning, bool changed, bool eligible) =>
        status != PatrolRuntimeStatus.Moving || planning || changed || !eligible;

    internal static bool ReturnToAnchor(bool returning, float squaredDistance) => squaredDistance > (returning ? 1.5f * 1.5f : 3f * 3f);

    internal static float Speed(bool run) => run ? 1f : .35f;

    internal static bool KeepPath(float secondsWithoutProgress) => secondsWithoutProgress < 3f;

    internal static bool SameRemainingCorners(int count, int index, int freshCount, Func<int, int, float> squaredDistance)
    {
        if (index < 0 || index >= count || freshCount < 2)
            return false;
        // Ignore a fresh path's origin, including a native cursor still on it.
        if (squaredDistance(index, 0) <= .0001f)
            index++;
        if (count - index != freshCount - 1)
            return false;
        for (var i = 1; i < freshCount; i++, index++)
            if (squaredDistance(index, i) > .0001f)
                return false;
        return true;
    }
}

// Measure progress along the remaining route, so sideways motion or repeated
// shuffling against scenery cannot indefinitely postpone a stalled-path retry.
internal sealed class EncounterPathProgress
{
    private float _bestRemaining;
    private float _progressTime;

    internal void Reset(float remaining, float now)
    {
        _bestRemaining = remaining;
        _progressTime = now;
    }

    internal bool Observe(float remaining, float now)
    {
        if (!float.IsFinite(remaining))
            return false;
        if (_bestRemaining - remaining >= .15f)
        {
            _bestRemaining = remaining;
            _progressTime = now;
        }
        return EncounterMovementPolicy.KeepPath(now - _progressTime);
    }
}
