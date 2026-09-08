namespace SeasonalPerks.Client.UI;

// Cache encoded bytes, not Unity objects: each screen still owns and releases its textures.
internal sealed class ImageRequestCache
{
    private readonly object _gate = new();
    private readonly Dictionary<string, LinkedListNode<(string Key, byte[] Bytes)>> _entries = new(StringComparer.Ordinal);
    private readonly LinkedList<(string Key, byte[] Bytes)> _recent = new();
    private readonly Dictionary<string, Task<byte[]>> _pending = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _slots;
    private readonly long _maxBytes;
    private readonly int _maxEntries;
    private long _bytes;

    internal ImageRequestCache(long maxBytes = 64 * 1024 * 1024, int maxEntries = 256, int concurrentRequests = 6)
    {
        if (maxBytes <= 0 || maxEntries <= 0 || concurrentRequests <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes), "Cache limits must be positive.");
        }
        _maxBytes = maxBytes;
        _maxEntries = maxEntries;
        _slots = new SemaphoreSlim(concurrentRequests);
    }

    internal Task<byte[]> GetAsync(string key, Func<Task<byte[]>> download)
    {
        TaskCompletionSource<byte[]> completion;
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var entry))
            {
                _recent.Remove(entry);
                _recent.AddFirst(entry);
                return Task.FromResult(entry.Value.Bytes);
            }
            if (_pending.TryGetValue(key, out var pending))
            {
                return pending;
            }
            completion = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending.Add(key, completion.Task);
        }
        _ = DownloadAsync(key, download, completion);
        return completion.Task;
    }

    // Only evict the bytes rejected by this consumer, never a newer replacement.
    internal void Reject(string key, byte[] bytes)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var entry) && ReferenceEquals(entry.Value.Bytes, bytes))
            {
                Remove(entry);
            }
        }
    }

    private async Task DownloadAsync(string key, Func<Task<byte[]>> download, TaskCompletionSource<byte[]> completion)
    {
        try
        {
            byte[] bytes;
            await _slots.WaitAsync().ConfigureAwait(false);
            try
            {
                bytes = await download().ConfigureAwait(false);
                if (bytes == null || bytes.Length == 0)
                {
                    throw new InvalidDataException("The seasonal image response was empty.");
                }
            }
            finally
            {
                _slots.Release();
            }
            lock (_gate)
            {
                if (bytes.LongLength <= _maxBytes)
                {
                    while (_recent.Last != null && (_bytes + bytes.LongLength > _maxBytes || _entries.Count >= _maxEntries))
                    {
                        Remove(_recent.Last);
                    }
                    _entries.Add(key, _recent.AddFirst((key, bytes)));
                    _bytes += bytes.LongLength;
                }
                _pending.Remove(key);
                completion.SetResult(bytes);
            }
        }
        catch (Exception exception)
        {
            lock (_gate)
            {
                _pending.Remove(key);
                completion.SetException(exception);
            }
        }
    }

    private void Remove(LinkedListNode<(string Key, byte[] Bytes)> entry)
    {
        _entries.Remove(entry.Value.Key);
        _recent.Remove(entry);
        _bytes -= entry.Value.Bytes.LongLength;
    }
}
