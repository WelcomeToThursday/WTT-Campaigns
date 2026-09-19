using System.Reflection;
using WTT.Campaigns.Client.Authoring.Console;
using WTT.Campaigns.UI.Controls;

namespace WTT.Campaigns.Tests;

internal static class EditorConsoleChecks
{
    private sealed class Host : IEditorConsoleHost
    {
        internal string? Block;
        internal int Calls;
        internal string[] LastArguments = [];
        public string[] Tools => ["Scene", "AI"];
        public string[] Windows => ["Console", "Editor controls"];

        public string? Unavailable(string command) => Block;

        public string Run(string command, string[] args)
        {
            Calls++;
            LastArguments = args;
            return command;
        }
    }

    internal static void Run(Action<bool, string> check)
    {
        var host = new Host();
        var commands = new EditorConsoleCommands(host);
        void Invalid(string text)
        {
            var before = host.Calls;
            var rejected = false;
            try
            {
                commands.Execute(text);
            }
            catch (ArgumentException)
            {
                rejected = true;
            }
            check(rejected && host.Calls == before, "Invalid console input never reaches an editor action: " + text);
        }
        check(
            EditorConsoleCommands.Parse(" window \"Editor controls\" show ").SequenceEqual(new[] { "window", "Editor controls", "show" }),
            "Console parser preserves quoted arguments"
        );
        check(commands.Execute("HeLp FoCuS").Contains("Frame"), "Help resolves command names without case sensitivity");
        check(commands.Execute("help").Contains("camera speed"), "Help includes command usage");
        foreach (var command in new[] { "clear", "status", "selection", "focus", "undo", "redo" })
            check(commands.Execute(command.ToUpperInvariant()) == command, "Console routes " + command);
        check(
            commands.Execute("TOOL scene") == "tool" && host.LastArguments[0] == "Scene",
            "Tool arguments use registered canonical names"
        );
        check(
            commands.Execute("window \"editor controls\" SHOW") == "window"
                && host.LastArguments.SequenceEqual(new[] { "Editor controls", "show" }),
            "Window commands support multi-word case-insensitive names"
        );
        foreach (var input in new[] { "camera speed", "camera speed 0.25", "camera speed 96", "camera speed 1e1" })
            check(commands.Execute(input) == "camera", "Valid camera command: " + input);
        foreach (
            var input in new[]
            {
                "badcommand",
                "status extra",
                "tool",
                "tool Missing",
                "window Console destroy",
                "window \"Console",
                "camera zoom",
                "camera speed NaN",
                "camera speed Infinity",
                "camera speed 0",
                "camera speed 97",
                "camera speed 1,5",
                "clear; status",
            }
        )
            Invalid(input);
        check(commands.Execute(" ") == "", "Empty input is a no-op");
        host.Block = "Resolve conflict.";
        var calls = host.Calls;
        check(
            commands.Execute("undo") == "Unavailable: Resolve conflict." && host.Calls == calls,
            "Unavailable commands do not call handlers"
        );
        check(commands.Complete("wi").SequenceEqual(new[] { "window" }), "Autocomplete completes a command name");
        check(
            commands.Complete("window \"ed").SequenceEqual(new[] { "window \"Editor controls\"" }),
            "Autocomplete handles incomplete quoted arguments"
        );
        check(
            commands
                .Complete("window Console ")
                .SequenceEqual(new[] { "window Console show", "window Console hide", "window Console toggle" }),
            "Autocomplete offers window actions"
        );
        check(
            commands.Complete("tool ").Length == 2 && commands.Complete("tool Missing").Length == 0,
            "Tool completion only offers supported choices"
        );
        check(commands.Complete("camera s").Single() == "camera speed", "Camera subcommand completion");
        check(commands.Hint("camera speed 2").StartsWith("camera speed [value]"), "Hints display usage while entering arguments");
        foreach (
            var state in new[]
            {
                (false, true, false, false, true, true),
                (true, false, false, false, true, true),
                (true, true, true, false, true, true),
                (true, true, false, true, true, true),
                (true, true, false, false, false, true),
            }
        )
            check(
                EditorConsoleAvailability.Check("undo", state.Item1, state.Item2, state.Item3, state.Item4, state.Item5, state.Item6)
                    != null,
                "Console guard rejects closed, retired, conflicted, previewing or empty history state"
            );
        check(EditorConsoleAvailability.Check("redo", true, true, false, false, true, false) != null, "Empty redo is unavailable");
        check(EditorConsoleAvailability.Check("undo", true, true, false, false, true, false) == null, "Ready undo is available");
        check(
            EditorConsoleAvailability.Check("focus", true, true, false, false, true, true, false) != null,
            "Focus is unavailable without a supported selection and camera"
        );

        var history = new EditorConsoleHistory();
        check(history.Move(-1, "draft") == "draft", "Empty history preserves unfinished input");
        history.Add("first");
        history.Add("second");
        check(history.Move(-1, "unfinished") == "second" && history.Move(-1, "second") == "first", "History moves backwards");
        check(history.Move(1, "first") == "second" && history.Move(1, "second") == "unfinished", "History restores the unfinished draft");
        for (var i = 0; i < 120; i++)
            history.Add("command " + i);
        var oldest = "";
        for (var i = 0; i < 150; i++)
            oldest = history.Move(-1, oldest);
        check(oldest == "command 20", "Command history retains at most 100 entries");

        using var buffer = new EditorConsoleBuffer();
        var feedback = new EditorConsoleFeedback();
        void PublishFeedback(string message, ConsoleSeverity severity) => buffer.Add("Editor", message, severity, campaigns: true);
        feedback.Report("Pick a scene object first.", ConsoleSeverity.Warning, PublishFeedback);
        feedback.Report("Pick a scene object first.", ConsoleSeverity.Warning, PublishFeedback);
        check(buffer.Snapshot().Length == 2, "Separate editor actions retain repeated feedback instead of losing the second action");
        check(
            buffer.Snapshot().All(e => e.Campaigns && e.Severity == ConsoleSeverity.Warning),
            "Editor feedback uses the default console source filter and supplied severity"
        );
        feedback.Report("", ConsoleSeverity.Info, PublishFeedback);
        check(
            feedback.Last == "" && buffer.Snapshot().Length == 2,
            "Clearing operation feedback neither logs an empty row nor clears console history"
        );
        feedback.Report("Preview failed.", ConsoleSeverity.Error, PublishFeedback);
        for (var i = 0; i < 100; i++)
            _ = feedback.Last;
        check(
            buffer.Snapshot().Length == 3 && buffer.Snapshot()[2].Severity == ConsoleSeverity.Error,
            "Reading feedback for presentation never republishes an error"
        );
        buffer.Clear();
        buffer.Add("Campaigns", "editor warning", ConsoleSeverity.Warning, campaigns: true);
        buffer.Add("Other", "game", ConsoleSeverity.Error);
        buffer.Add("Command", "status", command: true);
        var entries = buffer.Snapshot();
        check(
            EditorConsoleBuffer.Matches(entries[0], false, true, true, true, true, "EDITOR"),
            "Campaigns filter and search are case insensitive"
        );
        check(!EditorConsoleBuffer.Matches(entries[1], false, true, true, true, true, ""), "Default filter hides other client sources");
        check(
            EditorConsoleBuffer.Matches(entries[1], true, true, true, true, true, "")
                && !EditorConsoleBuffer.Matches(entries[1], true, true, true, false, true, ""),
            "All client mode retains severity filtering"
        );
        check(
            EditorConsoleBuffer.Matches(entries[2], false, false, false, false, false, "no match"),
            "Command responses bypass every log filter"
        );
        buffer.Clear();
        Parallel.For(0, 12000, i => buffer.Add("worker", i.ToString()));
        check(
            buffer.Snapshot().Length == 2000 && buffer.Discarded == 10000 && buffer.Bytes <= EditorConsoleBuffer.MaximumBytes,
            "Concurrent producers retain bounded entries and count discarded messages"
        );
        buffer.Clear();
        Parallel.For(0, 500, _ => buffer.Add("worker", new string('x', 40000)));
        check(
            buffer.Bytes <= EditorConsoleBuffer.MaximumBytes && buffer.Snapshot().Length < 2000 && buffer.Discarded > 0,
            "Text retention is bounded independently of entry count"
        );
        check(
            buffer.Snapshot().All(e => e.Message.Length <= 16384 && e.Message.EndsWith("[truncated]")),
            "Oversized log messages are visibly truncated"
        );
        buffer.Dispose();
        buffer.Add("late", "ignored");
        check(buffer.Snapshot().Length == 0 && buffer.Bytes == 0, "Disposed buffer ignores late producers and releases messages");

        var defaults = new EditorWindowLayout
        {
            Dock = EditorDockNode.Default(),
            Windows =
            [
                new() { Id = "Tool:Layouts", Visible = true },
                new() { Id = "Inspector" },
                new() { Id = "Console", Visible = false },
            ],
        };
        var old = new EditorWindowLayout
        {
            Dock = EditorDockNode.Default(),
            Windows =
            [
                new()
                {
                    Id = "Tool:Layouts",
                    Visible = true,
                    Width = 410,
                },
            ],
        };
        var restored = EditorWindowLayout.Restore(old, defaults)!;
        check(
            !restored.Windows.Single(w => w.Id == "Console").Visible && restored.Windows.Single(w => w.Id == "Tool:Layouts").Width == 410,
            "Older layouts restore their sizes with Console hidden"
        );
        check(
            EditorDockLayout.Minimum(EditorDockNode.Group("Console"), new HashSet<string> { "Console" }).Height
                == 300 + EditorDockLayout.TabHeight,
            "Docked console keeps enough space for command entry"
        );
        var consoleDock = EditorDockNode.Split("horizontal", EditorDockNode.Group("Console"), new() { Kind = "viewport" });
        check(
            EditorDockLayout.FitDisplay(consoleDock, new(0, 0, 800, 250), new HashSet<string> { "Console" }).Kind == "viewport",
            "An undersized display floats Console instead of compressing its command entry below the minimum"
        );
    }

    internal static void Listener(Assembly client, Action<bool, string> check)
    {
        var type = client.GetType("WTT.Campaigns.Client.Authoring.Console.EditorConsoleListener")!;
        var bep = type.GetInterfaces().Single(i => i.Name == "ILogListener").Assembly;
        var listeners = bep.GetType("BepInEx.Logging.Logger")!.GetProperty("Listeners")!.GetValue(null)!;
        var contains = listeners.GetType().GetMethod("Contains")!;
        var listener = Activator.CreateInstance(type, nonPublic: true)!;
        var buffer = type.GetField("Buffer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(listener)!;
        var snapshot = buffer.GetType().GetMethod("Snapshot", BindingFlags.NonPublic | BindingFlags.Instance)!;
        try
        {
            check((bool)contains.Invoke(listeners, [listener])!, "Console subscribes to the actual BepInEx listener collection");
            var source = Activator.CreateInstance(bep.GetType("BepInEx.Logging.ManualLogSource")!, ["WTT-Campaigns"]);
            var level = Enum.Parse(bep.GetType("BepInEx.Logging.LogLevel")!, "Warning");
            var args = Activator.CreateInstance(bep.GetType("BepInEx.Logging.LogEventArgs")!, ["listener message", level, source]);
            type.GetMethod("LogEvent")!.Invoke(listener, [source, args]);
            check(((Array)snapshot.Invoke(buffer, null)!).Length == 1, "Actual BepInEx events reach the console buffer without Unity");
            ((IDisposable)listener).Dispose();
            ((IDisposable)listener).Dispose();
            check(!(bool)contains.Invoke(listeners, [listener])!, "Console disposal removes the listener exactly once");
            type.GetMethod("LogEvent")!.Invoke(listener, [source, args]);
            check(((Array)snapshot.Invoke(buffer, null)!).Length == 0, "Late BepInEx events cannot repopulate a disposed console");
            ((IDisposable)source!).Dispose();
        }
        finally
        {
            ((IDisposable)listener).Dispose();
        }
    }
}
