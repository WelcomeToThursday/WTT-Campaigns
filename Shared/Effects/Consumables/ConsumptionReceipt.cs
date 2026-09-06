namespace SeasonalPerks.Shared.Effects.Consumables;

// One receipt per use operation, not per inventory item. A later use of the same
// bottle may refresh a buff, but later ticks of one use must not refresh it.
public sealed class ConsumptionReceipt
{
    public bool Applied { get; private set; }

    public bool Observe(float before, float after, bool interrupted)
    {
        if (Applied || interrupted || !ConsumableEffects.PositiveFinite(before) || after < 0 || float.IsNaN(after) || before <= after)
            return false;
        Applied = true;
        return true;
    }
}
