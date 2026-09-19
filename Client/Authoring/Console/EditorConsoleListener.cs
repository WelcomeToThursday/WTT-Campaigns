using BepInEx.Logging;

namespace WTT.Campaigns.Client.Authoring.Console;

internal sealed class EditorConsoleListener : ILogListener
{
    internal readonly EditorConsoleBuffer Buffer = new();
    internal readonly EditorConsoleHistory History = new();
    private int _disposed;

    internal EditorConsoleListener() => Logger.Listeners.Add(this);

    public void LogEvent(object sender, LogEventArgs args)
    {
        if (System.Threading.Volatile.Read(ref _disposed) != 0)
            return;
        try
        {
            var source = args.Source.SourceName;
            var severity =
                (args.Level & (LogLevel.Fatal | LogLevel.Error)) != 0 ? ConsoleSeverity.Error
                : (args.Level & LogLevel.Warning) != 0 ? ConsoleSeverity.Warning
                : (args.Level & LogLevel.Debug) != 0 ? ConsoleSeverity.Debug
                : ConsoleSeverity.Info;
            Buffer.Add(
                source,
                args.Data?.ToString() ?? "",
                severity,
                campaigns: source.StartsWith("WTT-Campaigns", StringComparison.OrdinalIgnoreCase)
            );
        }
        catch
        {
            // A third-party log object's ToString may throw. Never log recursively from a listener.
        }
    }

    public void Dispose()
    {
        if (System.Threading.Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        Logger.Listeners.Remove(this);
        Buffer.Dispose();
    }
}
