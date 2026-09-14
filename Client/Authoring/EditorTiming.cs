namespace WTT.Campaigns.Client.Authoring;

internal struct EditorTiming
{
    internal long Calls,
        Allocated,
        Ticks,
        MaxTicks;

    internal void Add(long ticks, long allocated)
    {
        Calls++;
        Allocated += Math.Max(0, allocated);
        Ticks += Math.Max(0, ticks);
        MaxTicks = Math.Max(MaxTicks, ticks);
    }
}
