namespace WTT.Campaigns.Shared.Spatial;

/// <summary>Rechecks the entire squad immediately before each native movement command.</summary>
public static class EncounterPatrolDispatch
{
    public static int Apply(
        IReadOnlyList<PatrolMovementCommand> commands,
        Func<bool> squadEligible,
        Action<PatrolMovementCommand> move,
        Action releaseOwnedNavigation
    )
    {
        var moved = 0;
        foreach (var command in commands)
        {
            if (!squadEligible())
            {
                releaseOwnedNavigation();
                break;
            }
            move(command);
            moved++;
        }
        return moved;
    }
}
