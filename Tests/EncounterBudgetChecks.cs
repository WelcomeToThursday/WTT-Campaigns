using WTT.Campaigns.Client.Encounters;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Tests;

internal static class EncounterBudgetChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var budget = new EncounterSpawnBudget(8, 2);
        check(budget.TryReserve("first", 5, 0), "First wave reserves its complete population before generation");
        check(!budget.TryReserve("first", 1, 0) && budget.Reserved == 5, "Repeated scheduling cannot reserve the same wave twice");
        check(!budget.TryReserve("second", 4, 0), "Concurrent waves cannot exceed the active-bot cap");
        check(budget.TryReserve("second", 3, 0), "Remaining capacity can admit a complete second wave");
        check(!budget.TryReserve("third", 1, 0), "Wave generation concurrency stays bounded");
        budget.Release("first");
        check(!budget.TryReserve("third", 1, 5), "Committed living bots replace reservations without freeing occupied capacity");
        check(budget.TryReserve("third", 1, 4), "A confirmed death makes capacity available for a waiting wave");
        budget.Release("second");
        budget.Release("second");
        check(budget.Reserved == 1, "Cancellation and cleanup release a reservation only once");
        try
        {
            budget.TryReserve("oversized", 9, 0);
            check(false, "Oversized wave cannot wait forever");
        }
        catch (InvalidOperationException)
        {
            check(budget.Reserved == 1, "Oversized wave fails explicitly without reserving partial capacity");
        }

        var single = new EncounterSpawnBudget(64, 1);
        check(
            single.TryReserve("one", 1, 0) && !single.TryReserve("two", 1, 0),
            "Default concurrency serializes generation even with free bot capacity"
        );
        single.Release("one");
        check(single.TryReserve("two", 1, 1), "Next wave can start after previous generation settles");
        var frame = new EncounterFrameBudget(1);
        check(frame.TryTake(10) && !frame.TryTake(10) && frame.TryTake(11), "Native activation begins at most once per frame");

        var encounter = new MapEncounter
        {
            Id = "encounter",
            Waves = new()
            {
                new()
                {
                    Id = "queued",
                    Roster = new()
                    {
                        new() { Id = "r", Count = 2 },
                    },
                },
            },
        };
        var waves = new EncounterWaveStateMachine(encounter);
        waves.TryActivate("start", 0);
        check(waves.ReadyWaves(0).Count == 1 && waves.ReadyWaves(1).Count == 0, "A budget-queued wave is enqueued only once");
        var checkpoint = waves.Capture(1);
        check(checkpoint.Waves[0].Status == EncounterWaveStatus.Pending, "Checkpoint stores a queued wave as unspawned");
        var replacement = new EncounterWaveStateMachine(encounter);
        replacement.Restore(checkpoint, 10, new Dictionary<string, string>());
        check(replacement.ReadyWaves(10).Count == 1, "Restored attempt schedules the queued wave once without stale queue entries");

        var route = new MapPatrolRoute { Completion = MapPatrolRoute.PingPong };
        for (var i = 0; i < 9; i++)
            route.Waypoints.Add(new() { Position = new() { X = i * 10 } });
        var state = new EncounterPatrolStateMachine(route);
        state.Start(new[] { "leader" });
        var snapshots = new[]
        {
            new PatrolBotSnapshot
            {
                BotId = "leader",
                Position = new() { X = 1 },
                ControlState = PatrolBotControlState.Eligible,
            },
        };
        var native = new Navigation();
        var quota = new EncounterFrameBudget(2);
        var currentFrame = 0;
        var navigation = new EncounterPlanningNavigation(native, () => quota.TryTake(currentFrame));
        var completed = false;
        PatrolUpdate update = null!;
        for (; currentFrame < 10 && !completed; currentFrame++)
        {
            var before = native.Calls;
            navigation.BeginPass();
            completed = state.TryUpdateBudgeted(snapshots, currentFrame * .01, navigation, out update);
            check(native.Calls - before <= 2, "A long route search cannot exceed the per-frame navigation quota");
            if (!completed)
                check(
                    state.Status == PatrolRuntimeStatus.Inactive && state.TargetWaypointIndex == -1 && state.LeaderId == "",
                    "Deferred navigation leaves patrol state unchanged rather than reporting an unreachable route"
                );
        }
        check(
            completed && native.Calls == 9 && update.Commands.Count == 1 && state.TargetWaypointIndex == 0,
            "A route longer than the frame budget eventually completes using cached results from its planning pass"
        );
        check(state.AcknowledgeWaypoint("leader", 0, 1), "Completed planning emits a normal patrol command");
        navigation.Clear();
        navigation.BeginPass();
        native.Reachable = false;
        completed = false;
        for (var retry = 0; retry < 10 && !completed; retry++)
        {
            currentFrame++;
            navigation.BeginPass();
            completed = state.TryUpdateBudgeted(snapshots, currentFrame, navigation, out update);
            if (!completed)
                check(
                    state.Status == PatrolRuntimeStatus.Moving && state.TargetWaypointIndex == 1,
                    "Deferred blocked-route re-entry preserves the existing patrol until all alternatives are checked"
                );
        }
        check(
            completed && state.Status == PatrolRuntimeStatus.Suspended && state.SuspensionReason == PatrolSuspensionReason.Unreachable,
            "A fresh planning pass rechecks changed geometry instead of reusing an old reachable result"
        );
        navigation.Clear();
        navigation.BeginPass();
        snapshots[0].ControlState = PatrolBotControlState.Combat;
        var calls = native.Calls;
        check(
            state.TryUpdateBudgeted(snapshots, currentFrame, navigation, out update)
                && update.SuspensionReason == PatrolSuspensionReason.Combat
                && update.Commands.Count == 0
                && calls == native.Calls,
            "Combat preempts patrol immediately without consuming navigation capacity"
        );
        var movement = new EncounterFrameBudget(2);
        check(
            movement.TryTake(currentFrame) && movement.TryTake(currentFrame) && !movement.TryTake(currentFrame),
            "Movement retains its own bounded quota independently of planning"
        );
    }

    private sealed class Navigation : IPatrolNavigation
    {
        internal int Calls;
        internal bool Reachable = true;

        public bool CanReach(SpatialVector from, SpatialVector to)
        {
            Calls++;
            return Reachable;
        }
    }
}
