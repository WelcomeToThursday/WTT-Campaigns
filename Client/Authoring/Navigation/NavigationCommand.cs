using System.Globalization;

namespace WTT.Campaigns.Client.Authoring.Navigation;

internal static class NavigationCommand
{
    internal static readonly string[] Actions =
    {
        "build",
        "scan",
        "status",
        "issues",
        "select",
        "cancel",
        "restore",
        "view",
        "hide",
        "report",
    };

    internal static void Validate(string[] args)
    {
        if (args.Length == 0 || args.Length > 2)
            throw new ArgumentException("Usage: navmesh <action> [argument]. Type help navmesh.");
        args[0] = args[0].ToLowerInvariant();
        if (Array.IndexOf(Actions, args[0]) < 0)
            throw new ArgumentException("Unknown navigation action. Choices: " + string.Join(", ", Actions));
        if (args[0] == "view")
        {
            if (args.Length == 2)
            {
                args[1] = args[1].ToLowerInvariant();
                if (args[1] is not "all" and not "floor")
                    throw new ArgumentException("Usage: navmesh view [all|floor]");
            }
        }
        else if (args[0] is "issues" or "select")
        {
            if (args[0] == "select" && args.Length != 2)
                throw new ArgumentException("Usage: navmesh select <issue number>");
            if (
                args.Length == 2
                && (!int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < 1 || value > 1000000)
            )
                throw new ArgumentException("Use a whole number from 1 to 1000000.");
        }
        else if (args.Length != 1)
            throw new ArgumentException("Usage: navmesh " + args[0]);
    }
}
