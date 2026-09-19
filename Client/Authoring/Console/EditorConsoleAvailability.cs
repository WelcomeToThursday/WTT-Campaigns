namespace WTT.Campaigns.Client.Authoring.Console;

internal static class EditorConsoleAvailability
{
    internal static string? Check(
        string command,
        bool open,
        bool active,
        bool conflict,
        bool preview,
        bool undo,
        bool redo,
        bool focus = true
    )
    {
        if (!open)
            return "Open the editor first.";
        if (!active)
            return "There is no active editor session.";
        if (conflict)
            return "Resolve the draft conflict first.";
        if (preview)
            return "Return from preview or walkthrough first.";
        if (command == "undo" && !undo)
            return "There is no edit to undo.";
        if (command == "redo" && !redo)
            return "There is no edit to redo.";
        if (command == "focus" && !focus)
            return "Select a visible scene object in the Scene workspace, with no placement or drag active.";
        return null;
    }
}
