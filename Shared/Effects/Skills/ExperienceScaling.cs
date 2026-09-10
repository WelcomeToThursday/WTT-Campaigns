namespace WTT.Campaigns.Shared.Effects.Skills;

public static class ExperienceScaling
{
    public static int Award(int amount, float multiplier)
    {
        return amount <= 0 || !(multiplier > 0) || float.IsInfinity(multiplier)
            ? amount
            : (int)Math.Min(int.MaxValue, Math.Floor((double)amount * multiplier));
    }

    public static int Total(int previous, int proposed, float multiplier)
    {
        var gain = (long)proposed - previous;
        if (gain <= 0 || gain > int.MaxValue)
        {
            return proposed;
        }

        return (int)Math.Min(int.MaxValue, (long)previous + Award((int)gain, multiplier));
    }
}
