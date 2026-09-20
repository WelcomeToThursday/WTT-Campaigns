using WTT.Campaigns.Client.Authoring.Console;
using WTT.Campaigns.Client.Authoring.Navigation;

namespace WTT.Campaigns.Tests;

internal static class NavigationExperimentChecks
{
    private sealed class Host : IEditorConsoleHost
    {
        public string[] Tools => [];
        public string[] Windows => [];
        internal int Calls;
        internal string[] Arguments = [];

        public string? Unavailable(string command) => null;

        public string Run(string command, string[] arguments)
        {
            Calls++;
            Arguments = arguments;
            return command;
        }
    }

    internal static void Run(Action<bool, string> check)
    {
        NavigationRecipeChecks.Run(check);
        NavigationPaintingChecks.Run(check);
        NavigationMeshDecoderChecks.Run(check);
        NavigationTreeGeometryChecks.Run(check);
        NavigationCollisionChecks.Run(check);
        var host = new Host();
        var commands = new EditorConsoleCommands(host);
        foreach (
            var valid in new[]
            {
                "navmesh SCAN",
                "navmesh BUILD",
                "navmesh issues",
                "navmesh issues 2",
                "navmesh select 1",
                "navmesh restore",
                "navmesh cancel",
                "navmesh view",
                "navmesh view ALL",
                "navmesh view floor",
                "navmesh report",
            }
        )
            check(commands.Execute(valid) == "navmesh", "Navigation console accepts: " + valid);
        commands.Execute("navmesh VIEW FLOOR");
        check(host.Arguments.SequenceEqual(new[] { "view", "floor" }), "Navigation arguments are canonical before dispatch");
        foreach (
            var invalid in new[]
            {
                "navmesh",
                "navmesh destroy",
                "navmesh bake full",
                "navmesh recover full",
                "navmesh apply",
                "navmesh inspect",
                "navmesh build full",
                "navmesh bake",
                "navmesh bake regional",
                "navmesh scan extra",
                "navmesh recover unlimited",
                "navmesh issues -1",
                "navmesh issues 0",
                "navmesh issues 2147483647",
                "navmesh select 1.5",
                "navmesh select",
                "navmesh restore all",
                "navmesh view nearby",
                "navmesh view floor 10",
            }
        )
        {
            var before = host.Calls;
            var rejected = false;
            try
            {
                commands.Execute(invalid);
            }
            catch (ArgumentException)
            {
                rejected = true;
            }
            check(rejected && host.Calls == before, "Malformed navigation command cannot mutate runtime: " + invalid);
        }
        check(commands.Complete("navmesh bu").Single() == "navmesh build", "Manual build completion");
        check(commands.Complete("navmesh bake ").Length == 0, "Removed automatic bake has no completion");
        check(commands.Complete("navmesh recover ").Length == 0, "Removed automatic recovery has no completion");

        var stamp = new NavigationBuildStamp("session-a", "layout-a", 10, 20);
        check(
            commands.Complete("navmesh view ").SequenceEqual(new[] { "navmesh view all", "navmesh view floor" }),
            "Surface view completion"
        );
        check(stamp.Equals(new NavigationBuildStamp("session-a", "layout-a", 10, 20)), "Unchanged build context accepts completion");
        check(stamp.ChangesSince(stamp) == "", "Unchanged navigation context has no invalidation reason");
        check(
            new NavigationBuildStamp("session-b", "layout-b", 11, 21).ChangesSince(stamp)
                == "editor session changed; selected layout changed; layout content revision 10 → 11; scene geometry revision 20 → 21",
            "Interrupted navigation reports every changed context component and both revision values"
        );
        check(
            new NavigationBuildStamp("session-a", "layout-a", 10, 21).ChangesSince(stamp) == "scene geometry revision 20 → 21",
            "Geometry-only invalidation does not blame the layout or editor session"
        );
        foreach (
            var stale in new[]
            {
                new NavigationBuildStamp("session-b", "layout-a", 10, 20),
                new NavigationBuildStamp("session-a", "layout-b", 10, 20),
                new NavigationBuildStamp("session-a", "layout-a", 11, 20),
                new NavigationBuildStamp("session-a", "layout-a", 10, 21),
            }
        )
        {
            check(!stamp.Equals(stale), "Session replacement, layout switch, undo/edit and geometry change invalidate pending bake");
        }
    }
}
