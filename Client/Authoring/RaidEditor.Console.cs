using System.Globalization;
using WTT.Campaigns.Client.Authoring.Console;
using WTT.Campaigns.Client.Authoring.Views;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Client.Authoring;

public sealed partial class RaidEditor
{
    private EditorConsoleListener? _console;
    private readonly EditorConsoleFeedback _editorFeedback = new();
    private string LastFeedback => _editorFeedback.Last;

    private void ReportFeedback(string message, ConsoleSeverity severity = ConsoleSeverity.Info) =>
        _editorFeedback.Report(message, severity, PublishEditorFeedback);

    private void PublishEditorFeedback(string message, ConsoleSeverity severity)
    {
        if (_console != null)
            _console.Buffer.Add("Editor", message, severity, campaigns: true);
        else
            Plugin.LogInfo("Editor: " + message);
    }

    private void StartConsole()
    {
        StopConsole();
        ReportFeedback("");
        _console = new EditorConsoleListener();
        _view?.Console.Bind(_console, new EditorConsoleCommands(new ConsoleHost(this)));
    }

    private void StopConsole()
    {
        _view?.Console.Unbind();
        _console?.Dispose();
        _console = null;
    }

    private void BindConsole(RaidEditorView view)
    {
        if (_console != null)
            view.Console.Bind(_console, new EditorConsoleCommands(new ConsoleHost(this)));
    }

    private sealed class ConsoleHost : IEditorConsoleHost
    {
        private readonly RaidEditor _editor;

        internal ConsoleHost(RaidEditor editor) => _editor = editor;

        public string[] Tools
        {
            get
            {
                var result = new List<string>();
                foreach (var tool in RaidEditorView.ToolIds)
                    if (_editor.ContentToolAllowed(tool))
                        result.Add(tool);
                return result.ToArray();
            }
        }
        public string[] Windows
        {
            get
            {
                var names = new List<string> { "Console", "Navigation", "Properties", "Environment", "Editor controls", "Loot configuration" };
                names.AddRange(Tools);
                return names.ToArray();
            }
        }

        public string? Unavailable(string command)
        {
            var e = _editor;
            if (command is "clear" or "status" or "selection" or "navmesh")
                return null;
            return EditorConsoleAvailability.Check(
                command,
                e._open && e._view?.Valid == true,
                e._session != null && !e._session.Retired,
                e._session?.Conflict != null,
                e.AiPreviewBusy || e._walking || e._session?.Previewing == true,
                e._session?.CanUndo == true,
                e._session?.CanRedo == true,
                command != "focus" || e.CanFrameSceneForCommand && e._camera
            );
        }

        public string Run(string command, string[] args)
        {
            var e = _editor;
            switch (command)
            {
                case "navmesh":
                    return e.RunNavigationCommand(args);
                case "clear":
                    e._console!.Buffer.Clear();
                    return "";
                case "status":
                    var session = e._session;
                    return session == null
                        ? "No editor session."
                        : $"Map: {session.Location}\nDraft: {session.DraftId}\nTool: {e._mode}\nConnection: {(session.Contacted ? "Contacted" : "Waiting")}\nStatus: {session.Status}\nPending changes: {session.Dirty}\nConflict: {session.Conflict != null}\nRetired: {session.Retired}";
                case "selection":
                    var point = e.Selected;
                    if (e.SceneSelectionTarget is { } target && target)
                        return $"Selection: {target.name}\nID: {point?.Id ?? e._selected}\nPosition: {target.position}\nRotation: {target.eulerAngles}\nScale: {target.lossyScale}";
                    if (point != null)
                        return $"Selection: {point.Id}\nType: {point.GetType().Name}\nPosition: {string.Join(", ", point.Position)}\nRotation: {string.Join(", ", point.Rotation)}"
                            + (point is MapObjectEdit obj ? "\nScale: " + string.Join(", ", obj.Scale) : "");
                    return e._selected.Length > 0
                        ? $"Selection: {e._selected}\nTool: {e._mode}\nNo spatial transform is available."
                        : "Nothing is selected.";
                case "tool":
                    e.ActivateTool(args[0]);
                    e._view!.Windows.BrowseCategory();
                    return "Active tool: " + e._mode;
                case "window":
                    var id = args[0] switch
                    {
                        "Properties" => "Inspector",
                        "Environment" => "EnvironmentMenu",
                        "Editor controls" => "Controls",
                        "Loot configuration" => "LootConfiguration",
                        "Console" => "Console",
                        "Navigation" => "Navigation",
                        _ => "Tool:" + args[0],
                    };
                    var visible = args[1] == "show" || args[1] == "toggle" && !e._view!.Windows.IsOpen(id);
                    if (!visible && id == "Console")
                        e._view!.ReleaseFocus();
                    e._view!.Windows.ShowPanel(id, visible);
                    return args[0] + (visible ? " shown." : " hidden.");
                case "focus":
                    if (!e.CanFrameSceneForCommand || !e._camera)
                        return "Unavailable: select a visible scene object in the Scene workspace, with no placement or drag active.";
                    e.FrameSceneSelectionCore();
                    return "Framed the selected scene object.";
                case "camera":
                    if (args.Length == 2)
                        e.SetCameraSpeed(float.Parse(args[1], CultureInfo.InvariantCulture));
                    return "Camera speed: " + e.CameraSpeed.ToString("0.##", CultureInfo.InvariantCulture) + " m/s";
                case "undo":
                case "redo":
                    e.CancelDrag();
                    e._session!.Undo(command == "redo");
                    return command == "undo" ? "Edit undone." : "Edit redone.";
                default:
                    throw new ArgumentException("Unknown command.");
            }
        }
    }
}
