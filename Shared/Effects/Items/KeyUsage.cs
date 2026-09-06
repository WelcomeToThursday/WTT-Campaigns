namespace SeasonalPerks.Shared.Effects.Items;

public static class KeyUsage
{
    // Live PerkRuntimeUtility.ConvertDurabilityMultiplierToUsageChance, RVA 0x4019390.
    public static float ConsumptionChance(float multiplier)
    {
        if (multiplier <= 0)
        {
            return 1;
        }

        return multiplier > 1 ? 1 / multiplier : 1 - multiplier;
    }
}
