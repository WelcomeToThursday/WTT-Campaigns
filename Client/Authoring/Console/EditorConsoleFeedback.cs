namespace WTT.Campaigns.Client.Authoring.Console;

// Action feedback is an event, not a rendered notice. Keep the latest text only for
// capture/playtest state that needs it; repainting the UI never publishes it again.
internal sealed class EditorConsoleFeedback
{
    internal string Last { get; private set; } = "";

    internal void Report(string message, ConsoleSeverity severity, Action<string, ConsoleSeverity> publish)
    {
        Last = message;
        if (!string.IsNullOrWhiteSpace(message))
            publish(message, severity);
    }
}
