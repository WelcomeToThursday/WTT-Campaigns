using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WTT.Campaigns.Client.Authoring.Console;

internal interface IEditorConsoleHost
{
    string? Unavailable(string command);
    string Run(string command, string[] arguments);
    string[] Tools { get; }
    string[] Windows { get; }
}

internal sealed class EditorConsoleCommands
{
    internal sealed class Command
    {
        internal readonly string Name,
            Usage,
            Description;
        internal readonly int Minimum,
            Maximum;

        internal Command(string name, string usage, string description, int minimum, int maximum)
        {
            Name = name;
            Usage = usage;
            Description = description;
            Minimum = minimum;
            Maximum = maximum;
        }
    }

    internal static readonly Command[] Registry =
    {
        new("help", "help [command]", "List commands or show usage.", 0, 1),
        new("clear", "clear", "Clear console output.", 0, 0),
        new("status", "status", "Show the current editor session.", 0, 0),
        new("tool", "tool <name>", "Activate an available editor tool.", 1, 1),
        new("window", "window <name> <show|hide|toggle>", "Show, hide or toggle an editor window.", 2, 2),
        new("selection", "selection", "Describe the current selection.", 0, 0),
        new("focus", "focus", "Frame a supported scene selection.", 0, 0),
        new("camera", "camera speed [value]", "Read or set camera speed (0.25–96 metres/second).", 1, 2),
        new("undo", "undo", "Undo the previous edit.", 0, 0),
        new("redo", "redo", "Redo the previous undone edit.", 0, 0),
        new(
            "navmesh",
            "navmesh <build|scan|status|issues [page]|select number|cancel|restore|view [all/floor]|hide|report>",
            "Build previews only saved Add/Block paint. Native navigation stays loaded. Restore clears owned edits; scan checks the painted region. Use Windows → Navigation for brushes and explicit connections.",
            1,
            2
        ),
    };
    private readonly IEditorConsoleHost _host;

    internal EditorConsoleCommands(IEditorConsoleHost host) => _host = host;

    private static bool Equal(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static Command? Find(string name)
    {
        foreach (var item in Registry)
            if (Equal(name, item.Name))
                return item;
        return null;
    }

    internal static string[] Parse(string text, bool partial = false)
    {
        var result = new List<string>();
        var token = new StringBuilder();
        var quoted = false;
        var started = false;
        foreach (var c in text)
        {
            if (c == '"')
            {
                quoted = !quoted;
                started = true;
            }
            else if (char.IsWhiteSpace(c) && !quoted)
            {
                if (started)
                {
                    result.Add(token.ToString());
                    token.Clear();
                    started = false;
                }
            }
            else
            {
                token.Append(c);
                started = true;
            }
        }
        if (quoted && !partial)
            throw new ArgumentException("Unclosed quote. Put multi-word arguments inside double quotes.");
        if (started)
            result.Add(token.ToString());
        else if (partial)
            result.Add("");
        return result.ToArray();
    }

    internal string Execute(string text)
    {
        var tokens = Parse(text);
        if (tokens.Length == 0)
            return "";
        var command = Find(tokens[0]) ?? throw new ArgumentException("Unknown command '" + tokens[0] + "'. Type help.");
        var args = new string[tokens.Length - 1];
        Array.Copy(tokens, 1, args, 0, args.Length);
        if (args.Length < command.Minimum || args.Length > command.Maximum)
            throw new ArgumentException("Usage: " + command.Usage);
        if (command.Name == "help")
        {
            if (args.Length == 1)
            {
                var help = Find(args[0]) ?? throw new ArgumentException("Unknown command '" + args[0] + "'. Type help.");
                return help.Usage + " — " + help.Description;
            }
            var helpText = new StringBuilder();
            foreach (var help in Registry)
                helpText.AppendLine(help.Usage + " — " + help.Description);
            return helpText.ToString().TrimEnd();
        }
        if (command.Name == "camera")
        {
            if (!Equal(args[0], "speed"))
                throw new ArgumentException("Usage: " + command.Usage);
            if (
                args.Length == 2
                && (
                    !float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var speed)
                    || float.IsNaN(speed)
                    || float.IsInfinity(speed)
                    || speed < .25f
                    || speed > 96
                )
            )
                throw new ArgumentException("Camera speed must be a finite number from 0.25 to 96.");
        }
        if (command.Name == "tool")
            args[0] = Resolve(args[0], _host.Tools, "tool");
        if (command.Name == "window")
        {
            args[0] = Resolve(args[0], _host.Windows, "window");
            args[1] = Resolve(args[1], new[] { "show", "hide", "toggle" }, "window action");
        }
        if (command.Name == "navmesh")
            Navigation.NavigationCommand.Validate(args);
        var unavailable = _host.Unavailable(command.Name);
        if (unavailable != null)
            return "Unavailable: " + unavailable;
        return _host.Run(command.Name, args);
    }

    private static string Resolve(string value, string[] choices, string kind)
    {
        foreach (var choice in choices)
            if (Equal(value, choice))
                return choice;
        throw new ArgumentException("Unknown or unavailable " + kind + " '" + value + "'. Choices: " + string.Join(", ", choices));
    }

    internal string Hint(string text)
    {
        var tokens = Parse(text, true);
        var command = Find(tokens[0]);
        return command == null
            ? "Enter a command. Type help to list commands; Tab completes, ↑/↓ recall history."
            : command.Usage + " — " + command.Description;
    }

    internal string[] Complete(string text)
    {
        var tokens = Parse(text, true);
        var choices = new List<string>();
        if (tokens.Length == 1 || tokens.Length == 2 && Equal(tokens[0], "help"))
            foreach (var command in Registry)
                choices.Add(command.Name);
        else if (tokens.Length == 2 && Equal(tokens[0], "tool"))
            choices.AddRange(_host.Tools);
        else if (tokens.Length == 2 && Equal(tokens[0], "window"))
            choices.AddRange(_host.Windows);
        else if (tokens.Length == 3 && Equal(tokens[0], "window"))
            choices.AddRange(new[] { "show", "hide", "toggle" });
        else if (tokens.Length == 2 && Equal(tokens[0], "camera"))
            choices.Add("speed");
        else if (tokens.Length == 2 && Equal(tokens[0], "navmesh"))
            choices.AddRange(Navigation.NavigationCommand.Actions);
        else if (tokens.Length == 3 && Equal(tokens[0], "navmesh") && Equal(tokens[1], "view"))
            choices.AddRange(new[] { "all", "floor" });
        var prefix = new StringBuilder();
        for (var i = 0; i < tokens.Length - 1; i++)
            prefix.Append(Quote(tokens[i])).Append(' ');
        var results = new List<string>();
        foreach (var choice in choices)
            if (choice.StartsWith(tokens[tokens.Length - 1], StringComparison.OrdinalIgnoreCase))
                results.Add(prefix + Quote(choice));
        return results.ToArray();
    }

    private static string Quote(string value) => value.IndexOf(' ') >= 0 ? "\"" + value + "\"" : value;
}

internal sealed class EditorConsoleHistory
{
    private readonly List<string> _items = new();
    private int _index;
    private string _draft = "";

    internal void Add(string command)
    {
        if (command.Trim().Length == 0)
            return;
        if (_items.Count == 0 || _items[_items.Count - 1] != command)
            _items.Add(command);
        if (_items.Count > 100)
            _items.RemoveAt(0);
        _index = _items.Count;
        _draft = "";
    }

    internal string Move(int direction, string current)
    {
        if (_index == _items.Count)
            _draft = current;
        _index = Math.Max(0, Math.Min(_items.Count, _index + direction));
        return _index == _items.Count ? _draft : _items[_index];
    }
}
