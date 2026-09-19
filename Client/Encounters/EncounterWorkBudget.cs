using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Client.Encounters;

internal sealed class EncounterFrameBudget(int limit)
{
    private int _frame = -1;
    private int _used;

    internal bool HasCapacity(int frame)
    {
        if (frame != _frame)
        {
            _frame = frame;
            _used = 0;
        }
        return _used < limit;
    }

    internal bool TryTake(int frame)
    {
        if (!HasCapacity(frame))
            return false;
        _used++;
        return true;
    }
}

internal sealed class EncounterSpawnBudget(int capacity, int concurrentWaves)
{
    private readonly Dictionary<string, int> _reservations = new(StringComparer.Ordinal);
    internal int Reserved { get; private set; }

    internal bool TryReserve(string wave, int count, int living)
    {
        if (count < 1 || count > capacity)
            throw new InvalidOperationException($"Wave requires {count} bots but the configured active-bot budget is {capacity}.");
        if (living < 0 || _reservations.ContainsKey(wave) || _reservations.Count >= concurrentWaves || living + Reserved + count > capacity)
            return false;
        _reservations.Add(wave, count);
        Reserved += count;
        return true;
    }

    internal void Release(string wave)
    {
        if (_reservations.TryGetValue(wave, out var count))
        {
            _reservations.Remove(wave);
            Reserved -= count;
        }
    }
}

// Results live only for one planning pass. Deferral is distinct from an unreachable path.
internal sealed class EncounterPlanningNavigation(IPatrolNavigation navigation, Func<bool> admit) : IPatrolNavigationBudget
{
    private readonly Dictionary<(float, float, float, float, float, float), bool> _results = new();
    public bool Deferred { get; private set; }

    internal void BeginPass() => Deferred = false;

    public bool CanReach(SpatialVector from, SpatialVector to)
    {
        var key = (from.X, from.Y, from.Z, to.X, to.Y, to.Z);
        if (_results.TryGetValue(key, out var result))
            return result;
        if (!admit())
        {
            Deferred = true;
            return false;
        }
        result = navigation.CanReach(from, to);
        _results.Add(key, result);
        return result;
    }

    internal void Clear() => _results.Clear();
}
