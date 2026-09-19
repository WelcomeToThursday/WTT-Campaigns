namespace WTT.Campaigns.Client.Authoring.Scenes;

// A completed page belongs to exactly one search/filter/page generation.
internal sealed class SceneCatalogRequestState
{
    internal string Key { get; private set; } = "";
    internal int Generation { get; private set; }
    internal bool Loading { get; private set; }

    internal int Begin(string key)
    {
        Key = key;
        Loading = true;
        return ++Generation;
    }

    internal bool IsCurrent(int generation, string key) => generation == Generation && key == Key;

    internal bool Finish(int generation, string key)
    {
        if (!IsCurrent(generation, key))
            return false;
        Loading = false;
        return true;
    }

    internal void Reset()
    {
        Generation++;
        Key = "";
        Loading = false;
    }
}
