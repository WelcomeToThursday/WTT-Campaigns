namespace WTT.Campaigns.Client.Authoring.Scenes;

// Shared with offline stress tests. Paths are materialized only on demand, never per UI refresh.
internal sealed class SceneObjectIndex<T>
    where T : class
{
    internal sealed class Entry
    {
        internal readonly T Target;
        internal readonly string Name,
            Id;
        internal string? Path;

        internal Entry(T target, string name, int id)
        {
            Target = target;
            Name = name;
            Id = id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    internal const int MaxEntries = 100000;
    internal const int MaxPathLength = 4096;
    internal const int MaxCharacters = 8 * 1024 * 1024;
    private readonly List<Entry> _entries = new();
    private readonly Dictionary<T, Entry> _byTarget = new();
    private readonly Dictionary<string, List<Entry>> _byName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, T?> _resolved = new(StringComparer.Ordinal);
    private readonly List<Entry> _results = new();
    private readonly Func<T, string?> _path;
    private readonly Func<T, bool> _alive;
    private string? _query;
    private int _characters;
    internal bool Complete { get; set; }
    internal bool Limited { get; private set; }
    internal int Count => _entries.Count;
    internal int RetainedCharacters => _characters;
    internal IReadOnlyList<Entry> Entries => _entries;

    internal SceneObjectIndex(Func<T, string?> path, Func<T, bool> alive)
    {
        _path = path;
        _alive = alive;
    }

    internal bool Add(T target, string name)
    {
        if (_byTarget.ContainsKey(target))
            return true;
        if (Limited || Count >= MaxEntries || name.Length > MaxPathLength || _characters + name.Length > MaxCharacters)
        {
            Limited = true;
            return false;
        }
        var entry = new Entry(target, name, Count);
        _entries.Add(entry);
        _byTarget.Add(target, entry);
        _characters += name.Length;
        if (!_byName.TryGetValue(name, out var same))
            _byName.Add(name, same = new());
        same.Add(entry);
        _query = null;
        _resolved.Clear();
        return true;
    }

    internal void Limit()
    {
        Limited = true;
        Complete = true;
    }

    internal string? Path(T target)
    {
        if (!_alive(target))
            return null;
        if (!_byTarget.TryGetValue(target, out var entry))
            return _path(target);
        if (entry.Path != null)
            return entry.Path;
        if (_characters >= MaxCharacters - MaxPathLength)
        {
            Limited = true;
            return null;
        }
        var path = _path(target);
        if (path == null || path.Length > MaxPathLength || _characters + path.Length > MaxCharacters)
        {
            Limited = true;
            return null;
        }
        _characters += path.Length;
        entry.Path = path;
        return path;
    }

    internal T? Unique(string? path)
    {
        if (!Complete || Limited || string.IsNullOrEmpty(path) || path!.Length > MaxPathLength)
            return null;
        if (_resolved.TryGetValue(path, out var cached))
            return cached != null && _alive(cached) ? cached : null;
        // Compare only objects with the same leaf name, not every transform in the map.
        var leaf = path.Substring(path.LastIndexOf('/') + 1);
        T? match = null;
        if (_byName.TryGetValue(leaf, out var candidates))
            foreach (var entry in candidates)
            {
                if (!_alive(entry.Target) || Path(entry.Target) != path)
                    continue;
                if (match != null)
                {
                    match = null;
                    break;
                }
                match = entry.Target;
            }
        if (Limited)
            return null; // An incomplete index must never claim a unique binding.
        if (_resolved.Count >= 256)
            _resolved.Clear();
        _resolved[path] = match;
        return match;
    }

    internal IReadOnlyList<Entry> Search(string query)
    {
        if (_query == query)
            return _results;
        _query = query;
        _results.Clear();
        foreach (var entry in _entries)
            if (
                _alive(entry.Target)
                && (
                    query.Length == 0
                    || entry.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                    || Path(entry.Target)?.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                )
            )
                _results.Add(entry);
        return _results;
    }

    internal void Clear()
    {
        _entries.Clear();
        _byTarget.Clear();
        _byName.Clear();
        _resolved.Clear();
        _results.Clear();
        _characters = 0;
        _query = null;
        Complete = Limited = false;
    }
}
