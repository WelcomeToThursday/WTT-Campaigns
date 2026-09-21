using WTT.Campaigns.Shared.Authoring;

namespace WTT.Campaigns.Client.Authoring.Scenes;

internal sealed class SceneSearchQuery
{
    private readonly string[] _terms;

    internal SceneSearchQuery(string search) => _terms = search.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    internal bool Matches(
        string? name,
        string? id = null,
        string? path = null,
        string? source = null,
        string? asset = null,
        IReadOnlyList<string>? aliases = null,
        IReadOnlyList<string>? sources = null
    )
    {
        foreach (var term in _terms)
            if (
                !Contains(name, term)
                && !Contains(id, term)
                && !ContainsPathTerm(path, term)
                && !ContainsPathTerm(source, term)
                && !ContainsPathTerm(asset, term)
                && !ContainsAny(aliases, term)
                && !ContainsAny(sources, term, path: true)
            )
                return false;
        return true;
    }

    private static bool Contains(string? value, string term) => value?.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;

    // Paths contain shared folders such as Assets/Content. A search for "tent"
    // must not match that folder on almost every object in the game.
    private static bool ContainsPathTerm(string? value, string term)
    {
        if (value == null)
            return false;
        for (var start = 0; start < value.Length; )
        {
            var index = value.IndexOf(term, start, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                return false;
            if (index == 0 || !char.IsLetterOrDigit(value[index - 1]))
                return true;
            start = index + 1;
        }
        return false;
    }

    internal int Compare(SceneCatalogEntry a, SceneCatalogEntry b)
    {
        var relevance = NameMatches(b.Name).CompareTo(NameMatches(a.Name));
        if (relevance != 0)
            return relevance;
        var name = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        return name != 0 ? name : string.CompareOrdinal(a.Id, b.Id);
    }

    private int NameMatches(string name)
    {
        var count = 0;
        foreach (var term in _terms)
            if (Contains(name, term))
                count++;
        return count;
    }

    private static bool ContainsAny(IReadOnlyList<string>? values, string term, bool path = false)
    {
        if (values != null)
            for (var i = 0; i < values.Count; i++)
                if (path ? ContainsPathTerm(values[i], term) : Contains(values[i], term))
                    return true;
        return false;
    }
}
