using System;
using System.Collections.Generic;

namespace WTT.Campaigns.Client.Authoring.Console;

internal enum ConsoleSeverity
{
    Info,
    Warning,
    Error,
    Debug,
}

internal sealed class ConsoleEntry
{
    internal readonly DateTime Time = DateTime.Now;
    internal readonly string Source,
        Message;
    internal readonly ConsoleSeverity Severity;
    internal readonly bool Command,
        Campaigns;

    internal ConsoleEntry(string source, string message, ConsoleSeverity severity, bool command, bool campaigns)
    {
        Source = source;
        Message = message;
        Severity = severity;
        Command = command;
        Campaigns = campaigns;
    }

    internal string Text => $"{Time:HH:mm:ss.fff} [{Severity}] [{Source}] {Message}";
    internal int Bytes => (Source.Length + Message.Length) * sizeof(char);
}

// Producers write directly into a bounded store. There is no unbounded pending queue.
// UI snapshots are created only on the main thread while the panel is visible.
internal sealed class EditorConsoleBuffer : IDisposable
{
    internal const int MaximumEntries = 2000,
        MaximumBytes = 2 * 1024 * 1024;
    private readonly object _gate = new();
    private readonly Queue<ConsoleEntry> _entries = new();
    private int _bytes;
    private long _version,
        _discarded;
    private bool _disposed;
    internal long Version
    {
        get
        {
            lock (_gate)
                return _version;
        }
    }
    internal long Discarded
    {
        get
        {
            lock (_gate)
                return _discarded;
        }
    }
    internal int Bytes
    {
        get
        {
            lock (_gate)
                return _bytes;
        }
    }

    internal void Add(
        string source,
        string message,
        ConsoleSeverity severity = ConsoleSeverity.Info,
        bool command = false,
        bool campaigns = false
    )
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            source = Limit(source, 128);
            message = Limit(message, 16384);
            var entry = new ConsoleEntry(source, message, severity, command, campaigns);
            while (_entries.Count > 0 && (_entries.Count >= MaximumEntries || _bytes + entry.Bytes > MaximumBytes))
            {
                _bytes -= _entries.Dequeue().Bytes;
                _discarded++;
            }
            _entries.Enqueue(entry);
            _bytes += entry.Bytes;
            _version++;
        }
    }

    private static string Limit(string value, int length) =>
        value.Length <= length ? value : value.Substring(0, length - 14) + " … [truncated]";

    internal ConsoleEntry[] Snapshot()
    {
        lock (_gate)
            return _entries.ToArray();
    }

    internal static bool Matches(ConsoleEntry entry, bool allClient, bool info, bool warning, bool error, bool debug, string search)
    {
        if (entry.Command)
            return true;
        if (!allClient && !entry.Campaigns)
            return false;
        if (
            !(
                entry.Severity switch
                {
                    ConsoleSeverity.Info => info,
                    ConsoleSeverity.Warning => warning,
                    ConsoleSeverity.Error => error,
                    _ => debug,
                }
            )
        )
            return false;
        return search.Length == 0
            || entry.Message.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
            || entry.Source.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    internal void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _bytes = 0;
            _discarded = 0;
            _version++;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            Clear();
        }
    }
}
