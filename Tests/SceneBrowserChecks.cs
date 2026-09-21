using WTT.Campaigns.Client.Authoring.Scenes;

namespace WTT.Campaigns.Tests;

internal static class SceneBrowserChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var random = new Random(731);
        for (var trial = 0; trial < 60; trial++)
        {
            var left = Enumerable.Range(0, random.Next(35)).Select(_ => random.Next(20)).Order().ToArray();
            var right = Enumerable.Range(0, random.Next(35)).Select(_ => random.Next(20)).Order().ToArray();
            var expected = left.Concat(right).Order().ToArray();
            for (var offset = 0; offset <= expected.Length + 7; offset += 7)
                check(
                    SceneCatalogPage.Merge(left, right, offset, 7, (a, b) => a.CompareTo(b)).SequenceEqual(expected.Skip(offset).Take(7)),
                    "Catalog pagination preserves merged order, ties, empty inputs and the final page"
                );
        }
        var levels = Enumerable.Range(0, 150000).Select(i => i * 2).ToArray();
        var local = Enumerable.Range(0, 2000).Select(i => i * 149 + 1).Order().ToArray();
        var comparisons = 0;
        var page = SceneCatalogPage.Merge(
            local,
            levels,
            149500,
            48,
            (a, b) =>
            {
                comparisons++;
                return a.CompareTo(b);
            }
        );
        check(page.SequenceEqual(local.Concat(levels).Order().Skip(149500).Take(48)), "Late catalog page matches the complete catalog");
        check(comparisons < 80 && page.Count == 48, "150000-entry catalog paging performs bounded work and retains only visible entries");
        check(SceneCatalogPage.Contains(levels, 299998, (a, b) => a.CompareTo(b)), "Off-page selection remains discoverable");
        check(!SceneCatalogPage.Contains(levels, 299999, (a, b) => a.CompareTo(b)), "A selection hidden by filtering is absent");
        var state = new SceneBrowserState();
        check(
            state.HideUnavailable && !state.ShowCatalogEntry("No mesh") && state.ShowCatalogEntry(""),
            "Catalog initially hides unavailable assets"
        );
        check(!state.ShowCatalogEntry("Checking native container loot mapping"), "Pending container validation is excluded until ready");
        state.HideUnavailable = false;
        check(state.ShowCatalogEntry("No mesh"), "Inclusive catalog preserves unavailable entries");
        state.Reset();
        check(!state.HideUnavailable, "Availability preference survives editor map resets");
        state.HideUnavailable = true;
        state.Filter = "Doors";
        state.Tab = "Existing";
        check(
            state.Filter == "All" && state.Matches("Props") && state.Matches("Doors"),
            "Catalog doors do not hide other existing scene objects"
        );
        state.Filter = "Containers";
        state.Tab = "Changes";
        check(
            state.Filter == "All" && state.Matches("Barriers") && state.Matches("Loot"),
            "Changes initially includes every object category independently of other tabs"
        );
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
            check(
                SceneBrowserState.Filters.Any(f => f == state.Filter && SceneBrowserState.Available(tab, f)),
                "The active filter always has a visible dropdown option in " + tab
            );
        }
        state.Reset();
        check(state.Tab == "Catalog" && state.Filter == "Props", "A new scene session resets catalog filters");
        state.Tab = "Changes";
        check(state.Filter == "All", "A new scene session resets changes to all categories");
    }
}
