namespace WTT.Campaigns.Client.Authoring.Scenes;

internal sealed class SceneBrowserState
{
    internal static readonly string[] Filters = { "All", "Props", "Containers", "Doors", "Loot", "Presets", "Barriers" };
    private readonly Dictionary<string, string> _filters = new();
    internal bool HideUnavailable { get; set; } = true;

    internal bool ShowCatalogEntry(string error) => !HideUnavailable || error.Length == 0;

    internal string Tab { get; set; } = "Catalog";
    internal string Filter
    {
        get =>
            _filters.TryGetValue(Tab, out var value) ? value
            : Tab == "Catalog" ? "Props"
            : "All";
        set
        {
            if (Available(Tab, value))
                _filters[Tab] = value;
        }
    }

    internal static bool Available(string tab, string filter) =>
        filter is "Props" or "Containers" or "Doors" or "Loot" || (tab == "Catalog" ? filter == "Presets" : filter is "All" or "Barriers");

    internal bool Matches(string kind) => Filter == "All" || Filter == kind;

    internal void Reset()
    {
        _filters.Clear();
        Tab = "Catalog";
    }
}
