using WTT.Campaigns.Client.Encounters;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Tests;

internal static class PatrolCurvePlanningChecks
{
    private sealed class RejectedShortcut : IPatrolNavigation
    {
        internal int Calls;

        public bool CanReach(SpatialVector from, SpatialVector to)
        {
            Calls++;
            return false;
        }
    }

    internal static void Run(Action<bool, string> check)
    {
        foreach (var mode in new[] { MapPatrolRoute.Loop, MapPatrolRoute.Stop, MapPatrolRoute.PingPong })
        foreach (var direction in mode == MapPatrolRoute.PingPong ? new[] { 1, -1 } : new[] { 1 })
        {
            var route = new MapPatrolRoute
            {
                Id = "curved",
                Completion = mode,
                Waypoints =
                [
                    new() { Id = "a" },
                    new()
                    {
                        Id = "b",
                        Position = new() { X = 10 },
                    },
                    new()
                    {
                        Id = "c",
                        Position = new() { X = 10, Z = 10 },
                    },
                ],
            };
            route.Spline = RouteSpline.Create(route.Waypoints, mode == MapPatrolRoute.Loop);
            var state = new EncounterPatrolStateMachine(route);
            state.Start(["leader", "follower"]);
            state.Restore(
                new()
                {
                    RouteId = route.Id,
                    Waypoint = 1,
                    Direction = direction,
                },
                0
            );
            var bots = new[]
            {
                new PatrolBotSnapshot
                {
                    BotId = "leader",
                    Position = new() { X = 9 },
                    ControlState = PatrolBotControlState.Eligible,
                },
                new PatrolBotSnapshot
                {
                    BotId = "follower",
                    Position = new(),
                    ControlState = PatrolBotControlState.Eligible,
                },
            };
            var generic = new RejectedShortcut();
            var followerStatus = EncounterPathStatus.Pending;
            var leaderChecks = 0;
            var followerChecks = 0;
            var allow = false;
            var planner = new EncounterPlanningNavigation(
                generic,
                () => allow,
                (bot, actualRoute, waypoint, actualDirection) =>
                {
                    check(
                        ReferenceEquals(actualRoute, route) && waypoint == 1 && actualDirection == direction,
                        "Curve planning receives the actual route, waypoint and traversal direction"
                    );
                    if (bot.BotId == "leader")
                    {
                        leaderChecks++;
                        return EncounterPathStatus.Complete;
                    }
                    followerChecks++;
                    return followerStatus;
                }
            );
            planner.BeginPass();
            check(!state.TryUpdateBudgeted(bots, 1, planner, out _) && leaderChecks == 0, "Authored path checks obey the planning quota");
            allow = true;
            planner.BeginPass();
            check(
                !state.TryUpdateBudgeted(bots, 2, planner, out _) && state.TargetWaypointIndex == 1,
                "An unfinished follower curve does not suspend or advance the squad"
            );
            followerStatus = EncounterPathStatus.Complete;
            planner.BeginPass();
            check(
                state.TryUpdateBudgeted(bots, 3, planner, out var ready)
                    && ready.Status == PatrolRuntimeStatus.Moving
                    && ready.Commands.Count == 2
                    && generic.Calls == 0,
                "A valid authored patrol proceeds even when the generic waypoint shortcut is rejected"
            );
            check(
                leaderChecks == 1 && followerChecks == 2,
                "Deferred curve validation retries only unfinished members within a planning pass"
            );

            followerStatus = EncounterPathStatus.Failed;
            planner.Clear();
            planner.BeginPass();
            check(
                state.TryUpdateBudgeted(bots, 4, planner, out var blocked)
                    && blocked.Status == PatrolRuntimeStatus.Suspended
                    && blocked.Commands.Count == 0
                    && blocked.TargetWaypointIndex == 1
                    && !blocked.Rejoined,
                "An invalid follower approach still suspends the whole squad without skipping its authored waypoint"
            );
            followerStatus = EncounterPathStatus.Complete;
            planner.Clear();
            planner.BeginPass();
            check(
                state.TryUpdateBudgeted(bots, 5, planner, out var resumed)
                    && resumed.Status == PatrolRuntimeStatus.Moving
                    && resumed.Commands.Count == 2,
                "A repaired authored approach resumes both bots at the saved target"
            );
            bots[0].ControlState = PatrolBotControlState.Combat;
            planner.Clear();
            planner.BeginPass();
            var before = leaderChecks + followerChecks;
            check(
                state.TryUpdateBudgeted(bots, 6, planner, out var combat)
                    && combat.Commands.Count == 0
                    && combat.SuspensionReason == PatrolSuspensionReason.Combat
                    && before == leaderChecks + followerChecks,
                "Combat preempts authored-route planning without spending path queries"
            );

            bots[0].ControlState = PatrolBotControlState.Eligible;
            route.Spline = null;
            planner.Clear();
            planner.BeginPass();
            state.TryUpdateBudgeted(bots, 7, planner, out _);
            check(generic.Calls > 0, "Legacy patrols continue using native waypoint reachability");
        }
    }
}
