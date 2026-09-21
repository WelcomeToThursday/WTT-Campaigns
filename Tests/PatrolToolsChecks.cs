using Newtonsoft.Json;
using WTT.Campaigns.Client.Authoring.Scenes;
using WTT.Campaigns.Client.Encounters;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Tests;

internal static class PatrolToolsChecks
{
    private sealed class Navigation(Func<SpatialVector, SpatialVector, bool> reachable) : IPatrolNavigation
    {
        public bool CanReach(SpatialVector from, SpatialVector to) => reachable(from, to);
    }

    private static MapPatrolRoute Route() =>
        new()
        {
            Id = "0123456789abcdef01234567",
            Name = "Patrol",
            Completion = MapPatrolRoute.PingPong,
            Waypoints = Enumerable
                .Range(0, 3)
                .Select(i => new SpatialCapture
                {
                    Id = $"0123456789abcdef0123456{i}",
                    Name = "Point " + i,
                    Position = new() { X = i * 10 },
                })
                .ToList(),
            WaitSeconds = [2, 5, 0],
        };

    private static PatrolBotSnapshot Bot(string id, float x = 0, PatrolBotControlState control = PatrolBotControlState.Eligible) =>
        new()
        {
            BotId = id,
            Position = new() { X = x },
            ControlState = control,
        };

    internal static void Run(Action<bool, string> check)
    {
        EncounterClearanceChecks.Run(check);
        var nav = new Navigation((_, _) => true);
        foreach (var direction in new[] { 1, -1 })
        foreach (var control in new[] { PatrolBotControlState.Combat, PatrolBotControlState.Searching, PatrolBotControlState.Recovery })
        {
            var route = Route();
            var state = new EncounterPatrolStateMachine(route);
            state.Start(["leader", "wing"]);
            state.Restore(
                new()
                {
                    RouteId = route.Id,
                    Waypoint = 1,
                    Direction = direction,
                },
                0
            );
            state.Update([Bot("leader"), Bot("wing")], 0, nav);
            state.Update([Bot("leader", 20, control), Bot("wing")], 1, nav);
            var update = state.Update([Bot("leader", 20), Bot("wing")], 50, nav);
            check(
                update.TargetWaypointIndex == 1 && state.Capture(50).Direction == direction && !update.Rejoined,
                $"{control} preserves target and direction {direction} despite a nearer waypoint"
            );
        }

        var waitingRoute = Route();
        var waiting = new EncounterPatrolStateMachine(waitingRoute);
        waiting.Start(["leader", "wing"]);
        waiting.Restore(
            new()
            {
                RouteId = waitingRoute.Id,
                Waypoint = 1,
                Direction = -1,
                WaitRemaining = 10,
            },
            0
        );
        waiting.Update([Bot("leader", control: PatrolBotControlState.Combat), Bot("wing")], 3, nav);
        waiting.Update([Bot("leader", control: PatrolBotControlState.Recovery), Bot("wing")], 20, nav);
        var paused = waiting.Capture(50);
        check(paused.WaitRemaining == 7 && paused.Direction == -1, "Repeated suspension freezes remaining wait for checkpoint capture");
        waiting.UpdateMembers(["leader", "wing", "new"]);
        check(waiting.Capture(50).WaitRemaining == 7, "Membership updates preserve paused waits");
        var survivors = new[] { Bot("leader"), Bot("wing"), Bot("new") };
        survivors[0].Alive = false;
        var resumed = waiting.Update(survivors, 100, nav);
        check(
            resumed.LeaderId == "wing" && resumed.Commands.Count == 0 && waiting.Capture(100).WaitRemaining == 7,
            "Leader replacement resumes the remaining wait without movement"
        );
        waiting.Update([Bot("leader", control: PatrolBotControlState.Searching), Bot("wing"), Bot("new")], 102, nav);
        check(waiting.Capture(200).WaitRemaining == 5, "A second interruption freezes only the unconsumed wait");
        var restored = new EncounterPatrolStateMachine(waitingRoute);
        restored.Start(["wing"]);
        restored.Restore(JsonConvert.DeserializeObject<PatrolCheckpoint>(JsonConvert.SerializeObject(waiting.Capture(200)))!, 500);
        check(
            restored.Update([Bot("wing")], 504, nav).Commands.Count == 0,
            "Suspended checkpoint wait survives serialization and a new clock"
        );
        check(
            restored.Update([Bot("wing")], 505, nav).TargetWaypointIndex == 1 && restored.Capture(505).Direction == -1,
            "Checkpoint resumes the original return leg after its wait"
        );
        restored.AcknowledgeWaypoint("wing", 1, 506);
        check(restored.TargetWaypointIndex == 0, "Return direction advances backward after resumption");

        var reentryRoute = Route();
        var reentry = new EncounterPatrolStateMachine(reentryRoute);
        reentry.Start(["leader", "wing"]);
        reentry.Restore(
            new()
            {
                RouteId = reentryRoute.Id,
                Waypoint = 2,
                Direction = -1,
            },
            0
        );
        var pair = new[] { Bot("leader"), Bot("wing", 1) };
        var common = new Navigation((from, to) => to.X != 20 && !(from.X == 1 && to.X == 0));
        var joined = reentry.Update(pair, 1, common);
        check(
            joined.Rejoined && joined.TargetWaypointIndex == 1 && joined.Commands.Count == 2 && reentry.Capture(1).Direction == -1,
            "Re-entry requires reachability for every survivor and keeps direction"
        );
        check(reentry.Capture(1).WaitRemaining == null, "Re-entry does not wait before arrival");
        var failed = reentry.Update(pair, 2, new Navigation((_, _) => false));
        check(
            failed.Status == PatrolRuntimeStatus.Suspended && failed.Commands.Count == 0 && reentry.TargetWaypointIndex == 1,
            "No common reachable waypoint retains the saved target and suspends"
        );
        check(reentry.Update(pair, 3, nav).TargetWaypointIndex == 1, "A recovered path resumes the retained target");

        var quotaState = new EncounterPatrolStateMachine(reentryRoute);
        quotaState.Start(["leader", "wing"]);
        quotaState.Restore(
            new()
            {
                RouteId = reentryRoute.Id,
                Waypoint = 2,
                Direction = -1,
            },
            0
        );
        quotaState.Update([Bot("leader", control: PatrolBotControlState.Combat), Bot("wing", 1)], 0, nav);
        var quota = new EncounterFrameBudget(1);
        var frame = 0;
        var budgeted = new EncounterPlanningNavigation(common, () => quota.TryTake(frame));
        var complete = false;
        for (; frame < 20 && !complete; frame++)
        {
            budgeted.BeginPass();
            complete = quotaState.TryUpdateBudgeted(pair, frame + 1, budgeted, out var update);
            if (!complete)
                check(
                    quotaState.Status == PatrolRuntimeStatus.Suspended
                        && quotaState.TargetWaypointIndex == 2
                        && quotaState.Capture(frame).Direction == -1
                        && !update.Rejoined,
                    "Deferred re-entry rolls back target, suspension, direction and notification"
                );
        }
        check(complete && quotaState.TargetWaypointIndex == 1, "Budgeted common re-entry eventually finishes");
        Arrival(check);
        Editing(check);
        Inspection(check);
    }

    private static void Arrival(Action<bool, string> check)
    {
        var flow = Route();
        flow.WaitSeconds = [];
        flow.Completion = MapPatrolRoute.Loop;
        check(
            EncounterMovementPolicy.Continuation(flow, 0, 1) == 1 && EncounterMovementPolicy.Continuation(flow, 2, 1) == 0,
            "Zero-wait loop paths continue through the endpoint and closure"
        );
        flow.Completion = MapPatrolRoute.PingPong;
        check(
            EncounterMovementPolicy.Continuation(flow, 1, -1) == 0
                && EncounterMovementPolicy.Continuation(flow, 0, -1) == 1
                && EncounterMovementPolicy.Continuation(flow, 2, 1) == 1,
            "Lookahead preserves the return direction and reverses only at ping-pong endpoints"
        );
        flow.Completion = MapPatrolRoute.Stop;
        check(
            EncounterMovementPolicy.Continuation(flow, 1, 1) == 2 && EncounterMovementPolicy.Continuation(flow, 2, 1) == -1,
            "Stop routes flow through interior points but never append beyond the terminal waypoint"
        );
        flow.WaitSeconds = [0, 2, 0];
        check(
            EncounterMovementPolicy.Continuation(flow, 1, 1) == -1 && EncounterMovementPolicy.Continuation(flow, 0, 1) == 1,
            "A nonzero wait blocks continuation at that point while the approach remains continuous"
        );
        var corners = EncounterMovementPolicy.JoinLegs(new[] { -2, -1, 0 }, new[] { 0, 1, 2 });
        check(
            corners.SequenceEqual(new[] { -2, -1, 0, 1, 2 }) && corners[^1] != 0,
            "A zero-wait waypoint becomes an intermediate native corner, avoiding native endpoint braking"
        );
        check(
            !EncounterMovementPolicy.CanJoin([new(0, 0, 0), new(10, 0, 0)], new(0, 0, 0)),
            "Ping-pong lookahead cannot put the final native destination on the current departure point"
        );
        check(
            EncounterMovementPolicy.CanJoin([new(3, 0, 0), new(10, 0, 0)], new(0, 0, 0)),
            "The return leg can be appended safely once the bot clears its departure point"
        );
        check(
            !EncounterMovementPolicy.CanJoin([new(0, 0, 0), new(10, 0, 0)], new(5, 0, 1)),
            "A looping continuation endpoint cannot prematurely complete an earlier approach segment"
        );
        check(
            !EncounterMovementPolicy.NeedsSquadPlan(PatrolRuntimeStatus.Moving, false, false, true),
            "Healthy travel performs no duplicate squad reachability search"
        );
        foreach (var status in new[] { PatrolRuntimeStatus.Inactive, PatrolRuntimeStatus.Waiting, PatrolRuntimeStatus.Suspended })
            check(
                EncounterMovementPolicy.NeedsSquadPlan(status, false, false, true),
                "Initialization, waits and suspension still update patrol state"
            );
        check(
            EncounterMovementPolicy.NeedsSquadPlan(PatrolRuntimeStatus.Moving, true, false, true)
                && EncounterMovementPolicy.NeedsSquadPlan(PatrolRuntimeStatus.Moving, false, true, true)
                && EncounterMovementPolicy.NeedsSquadPlan(PatrolRuntimeStatus.Moving, false, false, false),
            "Deferred passes, membership or path changes, and combat always bypass the healthy-travel shortcut"
        );
        var nav = new Navigation((_, _) => true);
        var pair = new[] { Bot("leader"), Bot("wing", 1) };
        foreach (var mode in new[] { MapPatrolRoute.Loop, MapPatrolRoute.PingPong, MapPatrolRoute.Stop })
        foreach (var explicitZeros in new[] { false, true })
        {
            var route = Route();
            route.Completion = mode;
            route.WaitSeconds = explicitZeros ? [0, 0, 0] : [];
            var state = new EncounterPatrolStateMachine(route);
            state.Start(["leader", "wing"]);
            state.Update(pair, 10, nav);
            check(!state.AcknowledgeWaypoint("wing", 0, 10), "An early follower cannot advance the squad route");
            check(state.AcknowledgeWaypoint("leader", 0, 10), "Leader arrival advances a zero-wait route");
            var next = state.Update(pair, 10, nav);
            check(
                next.Status == PatrolRuntimeStatus.Moving
                    && next.Commands.Count == 2
                    && next.Commands.All(c => c.WaypointIndex == 1)
                    && state.Capture(10).WaitRemaining == null,
                $"{mode} publishes the next waypoint at the same timestamp with {(explicitZeros ? "explicit" : "empty-list")} zero waits"
            );
            check(!state.AcknowledgeWaypoint("leader", 0, 10), "A stale arrival cannot advance the new target twice");
            state.AcknowledgeWaypoint("leader", 1, 10);
            state.Update(pair, 10, nav);
            state.AcknowledgeWaypoint("leader", 2, 10);
            var end = state.Update(pair, 10, nav);
            check(
                mode == MapPatrolRoute.Stop
                    ? end.Status == PatrolRuntimeStatus.Completed && end.Commands.Count == 0
                    : end.Status == PatrolRuntimeStatus.Moving && end.TargetWaypointIndex == (mode == MapPatrolRoute.Loop ? 0 : 1),
                "Zero-wait endings immediately loop, reverse, or stop as configured"
            );
        }
        var waited = new EncounterPatrolStateMachine(Route());
        waited.Start(["leader", "wing"]);
        waited.Update(pair, 0, nav);
        waited.AcknowledgeWaypoint("leader", 0, 0);
        check(
            waited.Update(pair, 0, nav).Commands.Count == 0
                && waited.Update(pair, 1.99, nav).Commands.Count == 0
                && waited.Update(pair, 2, nav).Commands.Count == 2,
            "Immediate arrival preserves authored nonzero waits"
        );

        var routeWithoutWaits = Route();
        routeWithoutWaits.WaitSeconds = [];
        var deferred = new EncounterPatrolStateMachine(routeWithoutWaits);
        deferred.Start(["leader", "wing"]);
        deferred.Update(pair, 0, nav);
        deferred.AcknowledgeWaypoint("leader", 0, 0);
        var admit = false;
        var budget = new EncounterPlanningNavigation(nav, () => admit);
        budget.BeginPass();
        check(
            !deferred.TryUpdateBudgeted(pair, 0, budget, out var pending)
                && pending.Commands.Count == 0
                && deferred.TargetWaypointIndex == 1
                && deferred.Status == PatrolRuntimeStatus.Moving,
            "Budget deferral after arrival preserves the successor without republishing the departed target"
        );
        admit = true;
        budget.BeginPass();
        check(
            deferred.TryUpdateBudgeted(pair, .01, budget, out var ready) && ready.Commands.All(c => c.WaypointIndex == 1),
            "Deferred zero-wait successor resumes when the navigation quota becomes available"
        );
        pair[0].ControlState = PatrolBotControlState.Combat;
        check(
            deferred.Update(pair, .01, nav).Commands.Count == 0 && deferred.Status == PatrolRuntimeStatus.Suspended,
            "Combat still preempts a freshly issued zero-wait leg"
        );
    }

    private static void Editing(Action<bool, string> check)
    {
        var route = Route();
        var selected = route.Waypoints[1];
        var fresh = new SpatialCapture
        {
            Id = "new",
            Position = new() { Y = 500 },
        };
        MapPatrolRouteEditing.Insert(route, 1, fresh);
        check(
            route.Waypoints[1] == fresh && route.WaitSeconds.SequenceEqual(new float[] { 2, 0, 5, 0 }),
            "Insertion adds a zero wait at the selected position"
        );
        MapPatrolRouteEditing.Move(route, 2, 0);
        check(route.Waypoints[0] == selected && route.WaitSeconds[0] == 5, "Reordering preserves waypoint identity and attached wait");
        var before = JsonConvert.SerializeObject(route);
        check(
            !MapPatrolRouteEditing.Move(route, 0, -1)
                && !MapPatrolRouteEditing.Move(route, 3, 4)
                && JsonConvert.SerializeObject(route) == before,
            "Boundary moves do not mutate the route"
        );
        MapPatrolRouteEditing.Reverse(route);
        check(
            route.Waypoints[^1] == selected && route.WaitSeconds[^1] == 5 && route.Completion == MapPatrolRoute.PingPong,
            "Reversing retains identities, waits and completion mode"
        );
        MapPatrolRouteEditing.Reverse(route);
        check(JsonConvert.SerializeObject(route) == before, "Reversing twice restores the entire route");
        route.WaitSeconds.Clear();
        MapPatrolRouteEditing.Insert(route, 0, new());
        MapPatrolRouteEditing.Move(route, 0, 2);
        MapPatrolRouteEditing.Reverse(route);
        check(route.WaitSeconds.Count == 0, "Zero waits retain the canonical empty list across edits");
        var layout = new MapLayout { PatrolRoutes = [Route()] };
        layout.PatrolRoutes[0].Waypoints[0].Position.Y = 500;
        layout.PatrolRoutes[0].Waypoints[1].Position.Y = 500;
        var saved = JsonConvert.DeserializeObject<MapLayout>(JsonConvert.SerializeObject(layout))!;
        var adapter = new DraftNavigation();
        check(MapEncounterRules.Errors(saved).Count == 0, "Off-mesh finite waypoint drafts save and reload");
        check(MapEncounterRules.Errors(saved, adapter, true, true).Count > 0, "Invalid draft cannot pass runtime validation");
        saved.PatrolRoutes[0].Waypoints[0].Position.Y = 0;
        check(
            MapEncounterRules.Errors(saved).Count == 0 && MapEncounterRules.Errors(saved, adapter, true, true).Count > 0,
            "Repairing one of several broken points remains a valid draft but cannot run"
        );
        saved.PatrolRoutes[0].Waypoints[1].Position.Y = 0;
        check(
            MapEncounterRules.Errors(saved, adapter, true, true).Count == 0,
            "A fully repaired route passes strict navigation validation"
        );
    }

    private sealed class DraftNavigation : IEncounterNavigation
    {
        public bool IsOnNavMesh(SpatialVector p) => p.Y == 0;

        public bool HasStandingClearance(SpatialVector p) => p.Y == 0;

        public bool HasCompletePath(SpatialVector a, SpatialVector b) => a.Y == 0 && b.Y == 0;
    }

    private static void Inspection(Action<bool, string> check)
    {
        var route = Route();
        var inspection = new PatrolRouteInspection();
        var calls = 0;
        EncounterPathResult Query(SpatialVector a, SpatialVector b)
        {
            calls++;
            return b.X < a.X ? new(EncounterPathStatus.Failed, reason: "Return blocked") : new(EncounterPathStatus.Complete, [a, b]);
        }
        inspection.Refresh(route, "route", 1, 1, 0, 0, Query);
        check(
            calls == 4 && inspection.Segments.Count == 4 && inspection.Segments[1].From == 1 && inspection.Segments[1].To == 0,
            "Ping-pong inspection queries both directions within four checks per frame"
        );
        check(
            inspection.Summary(false).Contains("Waypoint 2 → Waypoint 1: Return blocked")
                && !inspection.Summary(false).Contains("Complete route"),
            "Asymmetric failures identify the exact return segment outside detailed inspection"
        );
        inspection.Refresh(route, "route", 1, 1, 1, 1, Query);
        check(calls == 4, "Camera-only refresh reuses path results");
        inspection.Refresh(route, "waypoint", 1, 1, 1, 1, Query);
        check(calls == 8, "Changing selection invalidates cached diagnostics");
        inspection.Refresh(route, "waypoint", 2, 1, 1, 1, Query);
        check(calls == 8 && inspection.Pending, "Repeated same-frame invalidations cannot exceed the query budget");
        inspection.Refresh(route, "waypoint", 2, 1, 2, 1, Query);
        check(calls == 12 && !inspection.Pending, "Layout invalidation finishes on a later frame");
        inspection.Refresh(route, "waypoint", 2, 2, 3, 1, Query, false);
        check(calls == 12 && inspection.Pending, "Navigation invalidation waits for carving readiness");
        inspection.Refresh(route, "waypoint", 2, 2, 4, 1, Query);
        inspection.Refresh(route, "waypoint", 2, 2, 5, 3, Query);
        check(calls == 20, "Unchanged selection refreshes after two seconds");
        route.Completion = MapPatrolRoute.Loop;
        inspection.Refresh(route, "route", 3, 2, 6, 4, Query);
        check(
            inspection.Segments.Count == 3 && inspection.Segments[^1].From == 2 && inspection.Segments[^1].To == 0,
            "Loop diagnostics include the closing leg"
        );
        route.Waypoints[0].Position.X = float.NaN;
        inspection.Refresh(route, "route", 4, 2, 7, 4, Query);
        check(inspection.Segments[0].Result.Reason.Contains("coordinates"), "Invalid endpoints fail without native navigation queries");
        var distance = new EncounterPathResult(
            EncounterPathStatus.Complete,
            [
                new(),
                new() { X = 3, Z = 4 },
                new()
                {
                    X = 3,
                    Z = 4,
                    Y = 2,
                },
            ]
        );
        check(distance.Distance == 7, "Distance measures all three-dimensional path segments");
        route.Waypoints.Clear();
        inspection.Refresh(route, "route", 5, 2, 8, 4, Query);
        check(
            inspection.Summary(false).Contains("at least two") && inspection.Segments.Count == 0,
            "Incomplete routes clear old geometry and explain what is missing"
        );
    }
}
