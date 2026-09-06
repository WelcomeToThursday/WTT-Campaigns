namespace SeasonalPerks.Shared;

public static class TraderPricing
{
    // Keep catalogue decimals exact before converting back to the game's double count.
    // A float 1.2 would make an integral price slightly larger and cost an extra unit.
    public static double Scale(double count, decimal multiplier) =>
        (double)((decimal)count * multiplier);

    // EFT.Trading.Requisite rounds after multiplying by the requested quantity.
    public static double Required(double unitCount, int quantity) =>
        Math.Ceiling(unitCount * quantity);
}
