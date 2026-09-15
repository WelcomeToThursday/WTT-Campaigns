namespace WTT.Campaigns.Server.Seasons;

/// <summary>Restores locale values correctly when a reset overlaps two disposable snapshots.</summary>
internal sealed class IsolatedLocaleLayers
{
    private sealed class Entry
    {
        internal required Dictionary<string, string> Database;
        internal bool HadOriginal;
        internal string Original = "";
        internal readonly List<(object Owner, string Value)> Layers = [];
    }
    private readonly object _gate = new();
    private readonly Dictionary<(string Language, string Key), Entry> _entries = [];

    internal IDisposable Install(string language, Dictionary<string, string> database, IReadOnlyDictionary<string, string> values)
    {
        var owner = new object();
        var keys = values.Keys.Select(key => (Language: language, Key: key)).ToArray();
        lock (_gate)
        {
            foreach (var key in keys)
            {
                if (!_entries.TryGetValue(key, out var entry))
                {
                    var present = database.TryGetValue(key.Key, out var old);
                    _entries[key] = entry = new Entry { Database = database, HadOriginal = present, Original = old ?? "" };
                }
                entry.Layers.Add((owner, values[key.Key]));
                database[key.Key] = values[key.Key];
            }
        }
        return new Registration(() =>
        {
            lock (_gate)
            {
                foreach (var key in keys)
                {
                    if (!_entries.TryGetValue(key, out var entry)) continue;
                    entry.Layers.RemoveAll(layer => ReferenceEquals(layer.Owner, owner));
                    if (entry.Layers.Count > 0) entry.Database[key.Key] = entry.Layers[^1].Value;
                    else
                    {
                        if (entry.HadOriginal) entry.Database[key.Key] = entry.Original;
                        else entry.Database.Remove(key.Key);
                        _entries.Remove(key);
                    }
                }
            }
        });
    }
    private sealed class Registration(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
