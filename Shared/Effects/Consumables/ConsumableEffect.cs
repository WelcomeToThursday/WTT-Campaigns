namespace WTT.Campaigns.Shared.Effects.Consumables;

public sealed class ConsumableEffect
{
    public string Kind { get; }
    public float Duration { get; }
    public float Rate { get; }
    public IReadOnlyList<string> Targets { get; }

    internal ConsumableEffect(string kind, float duration, float rate, string[] targets)
    {
        Kind = kind;
        Duration = duration;
        Rate = rate;
        Targets = targets;
    }
}
