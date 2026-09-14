using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Tests;

internal static class EncounterRuntimeChecks
{
    private static string Id() => Guid.NewGuid().ToString("N")[..24];

    private static SpatialCapture Point(string name, float x, float z = 0) =>
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
                Z = z,
            },
        };

    internal static void Run(Action<bool, string> check)
    {
        Admission(check);
        Waves(check);
        RepeatedPreviews(check);
        Patrol(check);
    }

    private static void Admission(Action<bool, string> check)
    {
        var context = new EncounterRuntimeContext
        {
            SessionId = Id(),
            RaidId = Id(),
            LayoutId = Id(),
            LayoutRevision = 4,
            Mode = EncounterRuntimeModes.Preview,
            PreviewGeneration = Id(),
        };
        var admission = new EncounterSpawnAdmission();
        check(admission.CanActivate(context, out _), "Preview runtime context is admitted");
        check(
            admission.TryReserve(context, "encounter-a", "profile-a", "spawn-a", out var first, out _)
                && first != null
                && admission.ActiveCount == 1,
            "Admission creates one scoped reservation"
        );
        check(
            !admission.TryReserve(context, "encounter-b", "profile-a", "spawn-b", out _, out _),
            "A profile cannot be admitted twice in one preview generation"
        );
        check(admission.TryConsume(context, first!, out _), "Admission consumes a reservation once");
        check(!admission.TryConsume(context, first!, out _), "Admission tokens are single-use");
        check(
            admission.TryReserve(context, "encounter-b", "profile-b", "spawn-a", out var second, out _)
                && admission.TryConsume(context, second!, out _),
            "A finite wave can reuse an authored spawn anchor after prior activation consumes its reservation"
        );
        check(
            admission.TryReserve(context, "encounter-c", "profile-c", "spawn-c", out var stale, out _),
            "A second reservation can be held for invalidation testing"
        );
        check(
            admission.Invalidate(context) == 1 && !admission.TryConsume(context, stale!, out _),
            "Invalidation rejects late activation results"
        );
        check(
            !admission.CanActivate(
                new EncounterRuntimeContext { Mode = EncounterRuntimeModes.Mission, PublishedLayoutConfirmed = false },
                out _
            ),
            "Unconfirmed mission context is denied"
        );
        check(
            admission.CanActivate(
                new EncounterRuntimeContext
                {
                    SessionId = context.SessionId,
                    RaidId = context.RaidId,
                    LayoutId = context.LayoutId,
                    PreviewGeneration = Id(),
                    Mode = EncounterRuntimeModes.Mission,
                    PublishedLayoutConfirmed = true,
                },
                out _
            ),
            "A future mission context requires explicit published-layout confirmation"
        );
    }

    private static void Waves(Action<bool, string> check)
    {
        var spawnA = Id();
        var spawnA2 = Id();
        var spawnB = Id();
        var firstRoster = Id();
        var secondRoster = Id();
        var encounter = new MapEncounter
        {
            Id = Id(),
            Waves = new()
            {
                new MapEncounterWave
                {
                    Id = Id(),
                    Roster = new()
                    {
                        new MapEncounterRosterEntry
                        {
                            Id = firstRoster,
                            Count = 2,
                            SpawnPointIds = new() { spawnA, spawnA2 },
                        },
                    },
                },
                new MapEncounterWave
                {
                    Id = Id(),
                    DelaySeconds = 3,
                    WaitForPreviousWave = true,
                    Roster = new()
                    {
                        new MapEncounterRosterEntry
                        {
                            Id = secondRoster,
                            Count = 1,
                            SpawnPointIds = new() { spawnB },
                        },
                    },
                },
            },
        };
        var state = new EncounterWaveStateMachine(encounter);
        check(state.TryActivate("entry", 0), "Encounter activation is accepted once");
        check(!state.TryActivate("duplicate", 0), "Duplicate encounter activation is ignored");
        check(state.ReadyWaves(0).SequenceEqual(new[] { 0 }), "The first wave is ready immediately");
        check(state.TryBeginGeneration(0, "generation-a"), "First wave generation begins once");
        check(
            state.TryReserveBot(0, "generation-a", firstRoster, "profile-a", spawnA, out _),
            "First wave reserves an authored profile and spawn"
        );
        check(!state.TryCommitGeneration(0, "generation-a", 0, out _), "Incomplete wave generation fails atomically");
        check(state.IsFailed && state.Waves[0].Status == EncounterWaveStatus.Failed, "Wave generation failure halts the encounter");
        check(state.ReadyWaves(10).Count == 0, "A failed wave cannot release later waves");

        state.Reset();
        state.TryActivate("entry", 0);
        state.ReadyWaves(0);
        state.TryBeginGeneration(0, "generation-a");
        state.TryReserveBot(0, "generation-a", firstRoster, "profile-a", spawnA, out _);
        state.TryReserveBot(0, "generation-a", firstRoster, "profile-a2", spawnA2, out _);
        check(state.TryCommitGeneration(0, "generation-a", 1, out _), "Complete generation commits atomically");
        check(state.MarkBotDefeated(0, "profile-a", 2), "A known bot death is recorded");
        check(state.MarkBotDefeated(0, "profile-a2", 2), "Every admitted bot death is recorded");
        check(state.Waves[0].Status == EncounterWaveStatus.Completed, "A wave completes only after every admitted bot dies");
        check(state.ReadyWaves(4).Count == 0, "Death-gated next wave waits for its authored delay");
        check(state.ReadyWaves(5).SequenceEqual(new[] { 1 }), "Death-gated next wave releases after the authored delay");
        check(state.TryBeginGeneration(1, "generation-b"), "Second wave generation begins after clearance");
        state.TryReserveBot(1, "generation-b", secondRoster, "profile-b", spawnB, out _);
        check(state.TryCommitGeneration(1, "generation-b", 5), "Second wave commits after its own reservations");
        check(!state.MarkBotFailed(1, "missing-profile", "missing"), "Unknown missing profiles are not treated as deaths");
        check(
            state.MarkBotFailed(1, "profile-b", "activation lost") && state.IsFailed,
            "A missing active bot fails the encounter instead of completing it"
        );
    }

    private static void RepeatedPreviews(Action<bool, string> check)
    {
        var roster = new MapEncounterRosterEntry
        {
            Id = Id(),
            Count = 1,
            SpawnPointIds = new() { Id() },
        };
        var encounter = new MapEncounter
        {
            Id = Id(),
            Trigger = new() { Type = MapEncounterTrigger.Event, EventId = "manual" },
            Waves = new()
            {
                new MapEncounterWave
                {
                    Id = Id(),
                    Roster = new() { roster },
                },
            },
        };
        foreach (var withPatrol in new[] { false, true })
        {
            roster.PatrolRouteId = withPatrol ? Id() : "";
            for (var run = 0; run < 25; run++)
            {
                var state = new EncounterWaveStateMachine(encounter);
                check(state.ReadyWaves(100).Count == 0, "Fresh preview waits for activation even after time passes");
                check(state.TryActivate("event:manual", 100), "Every fresh preview accepts the same authored event");
                check(state.ReadyWaves(100).SequenceEqual(new[] { 0 }), "Event releases its wave with or without a patrol assignment");
                check(state.TryBeginGeneration(0, "generation-" + run), "Repeated preview can begin generation");
                check(
                    state.TryReserveBot(0, "generation-" + run, roster.Id, "profile-" + run, roster.SpawnPointIds[0], out _)
                        && state.TryCommitGeneration(0, "generation-" + run, 100, out _),
                    "Repeated preview can reuse the authored spawn and commit its bot"
                );
                check(!state.TryActivate("event:manual", 101), "Repeated event does not duplicate bots within the same preview");
                state.Cancel();
                check(state.ReadyWaves(200).Count == 0, "Retired preview cannot release more waves");
            }
        }
    }

    private static void Patrol(Action<bool, string> check)
    {
        var points = new[] { Point("A", 0), Point("B", 10), Point("C", 20) };
        var route = new MapPatrolRoute
        {
            Id = Id(),
            Waypoints = points.ToList(),
            WaitSeconds = new() { 2, 0, 0 },
            Completion = MapPatrolRoute.PingPong,
        };
        var patrol = new EncounterPatrolStateMachine(route);
        check(patrol.Start(new[] { "leader", "wingman" }), "Patrol starts with authored squad order");
        var nav = new TestPatrolNavigation();
        var eligible = new[]
        {
            new PatrolBotSnapshot
            {
                BotId = "leader",
                ControlState = PatrolBotControlState.Eligible,
                Position = Point("leader", 1).Position,
            },
            new PatrolBotSnapshot
            {
                BotId = "wingman",
                ControlState = PatrolBotControlState.Eligible,
                Position = Point("wingman", 2).Position,
            },
        };
        var update = patrol.Update(eligible, 0, nav);
        check(
            update.Status == PatrolRuntimeStatus.Moving && update.Commands.Count == 2 && update.TargetWaypointIndex == 0,
            "Eligible squads receive movement commands for the nearest reachable waypoint"
        );
        var combat = eligible.ToArray();
        combat[1] = new PatrolBotSnapshot
        {
            BotId = "wingman",
            ControlState = PatrolBotControlState.Combat,
            Position = combat[1].Position,
        };
        update = patrol.Update(combat, 1, nav);
        check(
            update.Status == PatrolRuntimeStatus.Suspended
                && update.SuspensionReason == PatrolSuspensionReason.Combat
                && update.Commands.Count == 0,
            "Combat suspends the whole squad without issuing patrol movement"
        );
        var unknown = eligible.ToArray();
        unknown[0] = new PatrolBotSnapshot
        {
            BotId = "leader",
            ControlState = PatrolBotControlState.Unknown,
            Position = unknown[0].Position,
        };
        update = patrol.Update(unknown, 2, nav);
        check(
            update.Status == PatrolRuntimeStatus.Suspended && update.SuspensionReason == PatrolSuspensionReason.UnknownState,
            "Unknown AI state never receives campaign control"
        );
        var resumed = eligible.ToArray();
        resumed[0] = new PatrolBotSnapshot
        {
            BotId = "leader",
            Alive = false,
            ControlState = PatrolBotControlState.Eligible,
            Position = resumed[0].Position,
        };
        update = patrol.Update(resumed, 3, nav);
        check(
            update.Status == PatrolRuntimeStatus.Moving && update.LeaderId == "wingman" && update.LeaderChanged,
            "Patrol elects the first surviving authored leader on resume"
        );
        check(patrol.AcknowledgeWaypoint("wingman", update.TargetWaypointIndex, 3), "Elected leader acknowledges a reached waypoint");
        check(patrol.Status == PatrolRuntimeStatus.Waiting, "Waypoint waits are honored before the next command");
        update = patrol.Update(resumed, 4, nav);
        check(update.Commands.Count == 0, "Waiting at a patrol waypoint emits no movement");
        update = patrol.Update(resumed, 6, nav);
        check(update.Commands.Count == 1 && update.TargetWaypointIndex == 1, "Patrol resumes after the authored wait");
        nav.BlockAll = true;
        update = patrol.Update(resumed, 7, nav);
        check(
            update.Status == PatrolRuntimeStatus.Suspended
                && update.SuspensionReason == PatrolSuspensionReason.Unreachable
                && update.Commands.Count == 0,
            "Unreachable rejoin reports suspension without teleporting bots"
        );
    }

    private sealed class TestPatrolNavigation : IPatrolNavigation
    {
        internal bool BlockAll;

        public bool CanReach(SpatialVector from, SpatialVector to) => !BlockAll;
    }
}
