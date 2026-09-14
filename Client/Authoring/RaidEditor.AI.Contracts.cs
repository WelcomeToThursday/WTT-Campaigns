using UnityEngine;
using WTT.Campaigns.Client.Encounters;
using WTT.Campaigns.Shared.Seasons;
using WTT.Campaigns.Shared.Spatial;
using ZLinq;

namespace WTT.Campaigns.Client.Authoring;

// The editor writes the shared records directly. Keep Unity-specific checks
// here so the authoring partial does not need a JSON-shaped compatibility layer.
internal static class RaidEditorAiContracts
{
    private static Func<MapLayout?>? _layoutProvider;
    private static readonly EncounterNavigation NavigationAdapter = new(() => _layoutProvider?.Invoke());

    internal static Func<MapLayout?>? LayoutProvider
    {
        get => _layoutProvider;
        set => _layoutProvider = value;
    }

    internal static string Id() => Guid.NewGuid().ToString("N")[..24];

    internal static bool HasAi(SeasonDefinition definition) =>
        definition != null && definition.MapLayouts.AsValueEnumerable().Any(MapEncounterRules.HasAi);

    internal static bool TryNav(Vector3 position, out Vector3 safe, float radius = .2f)
    {
        safe = position;
        // EncounterNavigation owns the installed-map tolerance and standing
        // clearance rules. The radius argument remains for callers compiled
        // against the earlier helper; authored coordinates are never snapped.
        _ = radius;
        if (
            !IsFinite(position)
            || !NavigationAdapter.IsOnNavMesh(
                new SpatialVector
                {
                    X = position.x,
                    Y = position.y,
                    Z = position.z,
                }
            )
            || !NavigationAdapter.HasStandingClearance(
                new SpatialVector
                {
                    X = position.x,
                    Y = position.y,
                    Z = position.z,
                }
            )
        )
            return false;
        return true;
    }

    internal static string NavigationError(Vector3 position) =>
        TryNav(position, out _) ? "" : "Position is not on a clear NavMesh standing area.";

    internal sealed class Navigation : IEncounterNavigation
    {
        public bool IsOnNavMesh(SpatialVector position) => TryNav(ToVector(position), out _);

        public bool HasStandingClearance(SpatialVector position) => NavigationAdapter.HasStandingClearance(position);

        public bool HasCompletePath(SpatialVector from, SpatialVector to) => NavigationAdapter.HasCompletePath(from, to);

        private static Vector3 ToVector(SpatialVector value) =>
            value == null ? Vector3.positiveInfinity : new Vector3(value.X, value.Y, value.Z);
    }

    internal static string RouteError(MapPatrolRoute route)
    {
        if (route?.Waypoints == null || route.Waypoints.Count < 2)
            return "";

        for (var i = 0; i < route.Waypoints.Count; i++)
        {
            var waypoint = route.Waypoints[i];
            if (waypoint?.Position?.Finite != true || !TryNav(ToVector(waypoint.Position), out _))
                return "Every patrol waypoint must be on a clear NavMesh standing area.";

            if (i + 1 >= route.Waypoints.Count)
            {
                if (route.Completion != MapPatrolRoute.Loop)
                    continue;
                if (route.Waypoints[0]?.Position?.Finite != true)
                    return "Every patrol waypoint must be on a clear NavMesh standing area.";
            }

            var next = route.Waypoints[(i + 1) % route.Waypoints.Count];
            if (next?.Position?.Finite != true || !CompletePath(waypoint.Position, next.Position))
                return i + 1 == route.Waypoints.Count && route.Completion == MapPatrolRoute.Loop
                    ? "Patrol waypoints must have a complete NavMesh path including its loop closure."
                    : "Patrol waypoints must have a complete NavMesh path.";

            if (route.Completion == MapPatrolRoute.PingPong && !CompletePath(next.Position, waypoint.Position))
                return "Patrol waypoints must have complete NavMesh return paths for ping-pong routes.";
        }

        return "";

        static Vector3 ToVector(SpatialVector value) => new(value.X, value.Y, value.Z);

        static bool CompletePath(SpatialVector from, SpatialVector to) => NavigationAdapter.HasCompletePath(from, to);
    }

    // Kept for callers compiled against the initial helper while all editor code
    // uses the typed route overload above. It validates the same forward and loop
    // semantics, including the reverse leg required by ping-pong patrols.
    internal static string RouteError(IEnumerable<Vector3> points, string completion)
    {
        var values = points.AsValueEnumerable().ToArray();
        if (values.Length < 2)
            return "";
        for (var i = 0; i < values.Length; i++)
        {
            if (!TryNav(values[i], out _))
                return "Every patrol waypoint must be on a clear NavMesh standing area.";
            if (i + 1 >= values.Length && !string.Equals(completion, MapPatrolRoute.Loop, StringComparison.Ordinal))
                continue;
            var next = values[(i + 1) % values.Length];
            if (
                !NavigationAdapter.HasCompletePath(
                    new SpatialVector
                    {
                        X = values[i].x,
                        Y = values[i].y,
                        Z = values[i].z,
                    },
                    new SpatialVector
                    {
                        X = next.x,
                        Y = next.y,
                        Z = next.z,
                    }
                )
            )
                return "Patrol waypoints must have a complete NavMesh path"
                    + (i + 1 == values.Length ? " including its loop closure." : ".");
            if (
                string.Equals(completion, MapPatrolRoute.PingPong, StringComparison.Ordinal)
                && !NavigationAdapter.HasCompletePath(
                    new SpatialVector
                    {
                        X = next.x,
                        Y = next.y,
                        Z = next.z,
                    },
                    new SpatialVector
                    {
                        X = values[i].x,
                        Y = values[i].y,
                        Z = values[i].z,
                    }
                )
            )
                return "Patrol waypoints must have complete NavMesh return paths for ping-pong routes.";
        }
        return "";
    }

    private static bool IsFinite(Vector3 value) =>
        !float.IsNaN(value.x)
        && !float.IsInfinity(value.x)
        && !float.IsNaN(value.y)
        && !float.IsInfinity(value.y)
        && !float.IsNaN(value.z)
        && !float.IsInfinity(value.z);
}
