using WTT.Campaigns.Client.Authoring.Scenes;

namespace WTT.Campaigns.Tests;

internal static class SceneBrowserChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var state = new SceneBrowserState();
        state.Filter = "Doors";
        state.Tab = "Existing";
        check(state.Filter == "All" && state.Matches("Props") && state.Matches("Doors"),
            "Catalog doors do not hide other existing scene objects");
        state.Filter = "Containers";
        state.Tab = "Changes";
        check(state.Filter == "All" && state.Matches("Barriers") && state.Matches("Loot"),
            "Changes initially includes every object category independently of other tabs");
        state.Filter = "Barriers";
        check(state.Matches("Barriers") && !state.Matches("Props"), "Changes filter narrows results explicitly");
        state.Tab = "Catalog";
        check(state.Filter == "Doors", "Catalog filter survives a round trip through the other tabs");
        state.Tab = "Existing";
        check(state.Filter == "Containers", "Existing objects retain their own filter");
        state.Filter = "Presets";
        check(state.Filter == "Containers", "Catalog-only presets cannot become a hidden existing-object filter");
        state.Tab = "Changes";
        check(state.Filter == "Barriers", "Changes retain their own filter");
        foreach (var tab in new[] { "Catalog", "Existing", "Changes" })
        {
            state.Tab = tab;
            check(SceneBrowserState.Filters.Any(f => f == state.Filter && SceneBrowserState.Available(tab, f)),
                "The active filter always has a visible dropdown option in " + tab);
        }
        state.Reset();
        check(state.Tab == "Catalog" && state.Filter == "Props", "A new scene session resets catalog filters");
        state.Tab = "Changes";
        check(state.Filter == "All", "A new scene session resets changes to all categories");
    }
}
