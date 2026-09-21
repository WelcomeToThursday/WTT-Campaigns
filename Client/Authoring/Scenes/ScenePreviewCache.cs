namespace WTT.Campaigns.Client.Authoring.Scenes;

// Owns only preview results; native icon textures can use a no-op release callback.
internal sealed class ScenePreviewCache<T>
    where T : class
{
    private readonly int _limit;
    private readonly Action<T> _release;
    private readonly Dictionary<string, T> _ready = new();
    private readonly Dictionary<string, string> _errors = new();

    // Dictionary enumeration is not insertion order after removals: reused
    // slots can make the newest image the next victim and flash a full page.
    private readonly List<string> _readyOrder = new();
    private readonly List<string> _errorOrder = new();
    private readonly HashSet<string> _requested = new();
    private readonly HashSet<string> _protected = new();
    internal int Generation { get; private set; }
    internal int Count => _ready.Count;
    internal long Hits { get; private set; }
    internal long Misses { get; private set; }

    internal ScenePreviewCache(int limit, Action<T> release)
    {
        _limit = limit;
        _release = release;
    }

    internal T? Get(string key)
    {
        if (!_ready.TryGetValue(key, out var value))
            return null;
        _readyOrder.Remove(key);
        _readyOrder.Add(key);
        return value;
    }

    internal void Protect(IEnumerable<string> keys)
    {
        _protected.Clear();
        foreach (var key in keys)
            _protected.Add(key);
    }

    internal string Error(string key) => _errors.TryGetValue(key, out var value) ? value : "";

    internal bool Request(string key)
    {
        if (Get(key) != null)
        {
            Hits++;
            return false;
        }
        if (_errors.ContainsKey(key) || !_requested.Add(key))
            return false;
        Misses++;
        return true;
    }

    internal void Abandon(string key) => _requested.Remove(key);

    internal void Retry(string key)
    {
        _errors.Remove(key);
        _errorOrder.Remove(key);
        _requested.Remove(key);
    }

    internal bool Complete(int generation, string key, T value, string protectedKey)
    {
        if (generation != Generation)
        {
            _release(value);
            return false;
        }
        _requested.Remove(key);
        if (_ready.TryGetValue(key, out var old))
        {
            _release(old);
            _readyOrder.Remove(key);
        }
        else if (_ready.Count >= _limit)
        {
            string? victim = null;
            foreach (var candidate in _readyOrder)
                if (candidate != protectedKey && !_protected.Contains(candidate))
                {
                    victim = candidate;
                    break;
                }
            if (victim == null)
            {
                _release(value);
                return false;
            }
            _release(_ready[victim]);
            _ready.Remove(victim);
            _readyOrder.Remove(victim);
        }
        _ready[key] = value;
        _readyOrder.Add(key);
        _errors.Remove(key);
        _errorOrder.Remove(key);
        return true;
    }

    internal void Fail(int generation, string key, string error)
    {
        if (generation != Generation)
            return;
        _requested.Remove(key);
        _errorOrder.Remove(key);
        if (!_errors.ContainsKey(key) && _errors.Count >= _limit)
        {
            var first = _errorOrder.Count > 0 ? _errorOrder[0] : null;
            if (first != null)
            {
                _errors.Remove(first);
                _errorOrder.RemoveAt(0);
            }
        }
        _errors[key] = error;
        _errorOrder.Add(key);
    }

    internal void Clear()
    {
        Generation++;
        foreach (var value in _ready.Values)
            _release(value);
        _ready.Clear();
        _readyOrder.Clear();
        _errors.Clear();
        _errorOrder.Clear();
        _requested.Clear();
        _protected.Clear();
        Hits = Misses = 0;
    }
}
