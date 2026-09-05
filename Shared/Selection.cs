namespace SeasonalPerks.Shared;

public static class Selection
{
    public static string? Validate(
        Catalogue catalogue,
        IEnumerable<string> ids,
        Rules rules,
        IReadOnlyDictionary<string, string> unavailable
    )
    {
        var values = ids.ToList();
        var chosen = new HashSet<string>(values, StringComparer.Ordinal);
        if (values.Count != chosen.Count)
        {
            return "A modifier was selected more than once.";
        }

        var index = catalogue.Personal.ToDictionary(p => p.Id);
        foreach (var id in chosen)
        {
            if (!index.TryGetValue(id, out var perk))
            {
                return "Unknown personal modifier: " + id;
            }

            if (unavailable.TryGetValue(id, out var reason))
            {
                return reason;
            }

            if (perk.Conflicts.Any(chosen.Contains))
            {
                return "Two selected modifiers cannot be used together.";
            }
        }
        if (rules.EnforceBudget && Balance(catalogue, chosen, rules.StartingPoints) < 0)
        {
            return "Select more drawbacks or remove a benefit to balance your points.";
        }

        return null;
    }

    public static long Balance(Catalogue catalogue, IEnumerable<string> ids, int startingPoints)
    {
        var selected = new HashSet<string>(ids);
        return startingPoints
            + catalogue
                .Personal.Where(p => selected.Contains(p.Id))
                .Sum(p => (long)(p.Points ?? 0));
    }
}
