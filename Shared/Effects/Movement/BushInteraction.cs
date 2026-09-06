namespace SeasonalPerks.Shared.Effects.Movement;

public static class BushInteraction
{
    // Scale the lost speed, not the remaining speed. Native RefreshObstacleRestrictions
    // uses this formula only while a tree trigger is occupied.
    public static float SpeedLimit(float original, float multiplier)
    {
        return multiplier.Equals(1f) ? original : 1f - (1f - original) * multiplier;
    }
}
