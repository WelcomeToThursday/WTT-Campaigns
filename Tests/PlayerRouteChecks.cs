using WTT.Campaigns.Client.Authoring.Scenes;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Tests;

internal static class PlayerRouteChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var route = new MapLayout
        {
            Start = new() { Id = "start" },
            Exit = new() { Id = "exit", Name = "Gate" },
        };
        var a = new MapVolume { Id = "a", Name = "Courtyard" };
        var b = new MapVolume { Id = "b", Name = "Stairs" };
        var c = new MapVolume { Id = "c", Name = "Landing" };
        PlayerRoute.Insert(route, route.Start.Id, a);
        PlayerRoute.Insert(route, route.Exit.Id, c);
        PlayerRoute.Insert(route, a.Id, b);
        check(
            route.Checkpoints.SequenceEqual(new[] { a, b, c }),
            "Inserting into a route preserves traversal order and existing checkpoint identities"
        );
        check(route.Start.Id == "start" && route.Exit.Id == "exit", "Checkpoint insertion preserves the route endpoints");
        check(PlayerRoute.Progress(route, 0) == "Checkpoint 1 / 3 · Courtyard", "Walkthrough names the first pending checkpoint");
        check(PlayerRoute.Progress(route, 2) == "Checkpoint 3 / 3 · Landing", "Walkthrough names the last pending checkpoint");
        check(
            PlayerRoute.Progress(route, 3) == "Head to exit · Gate",
            "Reaching the last checkpoint directs the player to the exit instead of checkpoint 4 of 3"
        );
        check(PlayerRoute.Progress(route, 4) == "Route complete", "Walkthrough completes only after reaching the exit");
    }
}
