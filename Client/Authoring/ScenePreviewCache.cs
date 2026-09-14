namespace WTT.Campaigns.Client.Authoring;

// Owns only preview results; native icon textures can use a no-op release callback.
internal sealed class ScenePreviewCache<T>
    where T : class
{
    private readonly int _limit;
    private readonly Action<T> _release;
    private readonly Dictionary<string, T> _ready = new();
    private readonly Dictionary<string, string> _errors = new();
    private readonly HashSet<string> _requested = new();
    internal int Generation { get; private set; }

    internal ScenePreviewCache(int limit, Action<T> release)
    {
        _limit = limit;
        _release = release;
    }

    internal T? Get(string key) => _ready.TryGetValue(key, out var value) ? value : null;

    internal string Error(string key) => _errors.TryGetValue(key, out var value) ? value : "";

    internal bool Request(string key) => !_ready.ContainsKey(key) && !_errors.ContainsKey(key) && _requested.Add(key);

    internal void Abandon(string key) => _requested.Remove(key);

    internal void Retry(string key)
    {
        _errors.Remove(key);
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
            _release(old);
        else if (_ready.Count >= _limit)
        {
            string? victim = null;
            foreach (var candidate in _ready.Keys)
                if (candidate != protectedKey)
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
        }
        _ready[key] = value;
        _errors.Remove(key);
        return true;
    }

    internal void Fail(int generation, string key, string error)
    {
        if (generation != Generation)
            return;
        _requested.Remove(key);
        if (_errors.Count >= _limit)
        {
            string? first = null;
            foreach (var entry in _errors.Keys)
            {
                first = entry;
                break;
            }
            if (first != null)
                _errors.Remove(first);
        }
        _errors[key] = error;
    }

    internal void Clear()
    {
        Generation++;
        foreach (var value in _ready.Values)
            _release(value);
        _ready.Clear();
        _errors.Clear();
        _requested.Clear();
    }
}
