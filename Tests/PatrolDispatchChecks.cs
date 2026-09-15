using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Tests;

internal static class PatrolDispatchChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var state = new EncounterPatrolStateMachine(
            new MapPatrolRoute
            {
                Waypoints = new()
                {
                    new() { Position = new() },
                    new() { Position = new() { X = 10 } },
                },
            }
        );
        state.Start(new[] { "leader", "wingman" });
        var commands = state
            .Update(
                new[]
                {
                    new PatrolBotSnapshot
                    {
                        BotId = "leader",
                        ControlState = PatrolBotControlState.Eligible,
                        Position = new(),
                    },
                    new PatrolBotSnapshot
                    {
                        BotId = "wingman",
                        ControlState = PatrolBotControlState.Eligible,
                        Position = new(),
                    },
                },
                0,
                new Navigation()
            )
            .Commands;
        var eligible = true;
        var moved = new List<string>();
        var released = 0;
        var count = EncounterPatrolDispatch.Apply(
            commands,
            () => eligible,
            command =>
            {
                moved.Add(command.BotId);
                // A native update between commands detects an enemy for a squad member.
                eligible = false;
            },
            () => released++
        );
        check(
            count == 1 && moved.SequenceEqual(new[] { "leader" }) && released == 1,
            "Combat beginning during patrol dispatch prevents further commands and releases owned navigation"
        );
        count = EncounterPatrolDispatch.Apply(
            commands,
            () => false,
            _ => throw new Exception("Unknown state received movement"),
            () => released++
        );
        check(count == 0 && released == 2, "Unknown squad state yields before the first native movement command");
    }

    private sealed class Navigation : IPatrolNavigation
    {
        public bool CanReach(SpatialVector from, SpatialVector to) => true;
    }
}
