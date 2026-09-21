namespace WTT.Campaigns.Client.Missions;

/// <summary>Raid countdown independent of the world time of day and objective event timers.</summary>
internal sealed class MissionCountdown
{
    private double? _remaining;
    private double _resumedAt;
    internal bool Paused { get; private set; } = true;

    internal MissionCountdown(double? seconds) => _remaining = seconds;

    internal double? Remaining(double now) =>
        _remaining.HasValue ? Math.Max(0, _remaining.Value - (Paused ? 0 : Math.Max(0, now - _resumedAt))) : null;

    internal void Pause(double now)
    {
        if (Paused)
            return;
        _remaining = Remaining(now);
        Paused = true;
    }

    internal void Resume(double now)
    {
        if (!Paused)
            return;
        _resumedAt = now;
        Paused = false;
    }

    internal void Restore(double? seconds, double now)
    {
        _remaining = seconds;
        _resumedAt = now;
    }

    internal static string Text(double? seconds)
    {
        if (!seconds.HasValue)
            return "∞";
        var remaining = TimeSpan.FromSeconds(Math.Ceiling(Math.Max(0, seconds.Value)));
        return $"{(int)remaining.TotalHours}:{remaining.Minutes:00}:{remaining.Seconds:00}";
    }
}
