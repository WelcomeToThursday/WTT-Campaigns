using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Tests;

internal static class PatrolDirectionChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var route = new MapPatrolRoute
        {
            Id = Guid.NewGuid().ToString("N")[..24],
            Name = "One-way link",
            Completion = MapPatrolRoute.PingPong,
            Waypoints = new()
            {
                new()
                {
                    Id = Guid.NewGuid().ToString("N")[..24],
                    Position = new() { X = 0 },
                },
                new()
                {
                    Id = Guid.NewGuid().ToString("N")[..24],
                    Position = new() { X = 1 },
                },
            },
        };
        var layout = new MapLayout { PatrolRoutes = new() { route } };
        check(
            MapEncounterRules.Errors(layout, new OneWayNavigation()).Any(e => e.Contains("return segment")),
            "Ping-pong validation rejects a one-way NavMesh connection"
        );
        route.Completion = MapPatrolRoute.Stop;
        check(
            !MapEncounterRules.Errors(layout, new OneWayNavigation()).Any(e => e.Contains("segment")),
            "Stop-at-end routes only require forward traversal"
        );
    }

    private sealed class OneWayNavigation : IEncounterNavigation
    {
        public bool IsOnNavMesh(SpatialVector point) => true;

        public bool HasStandingClearance(SpatialVector point) => true;

        public bool HasCompletePath(SpatialVector from, SpatialVector to) => to.X >= from.X;
    }
}
