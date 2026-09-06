using SeasonalPerks.Shared;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace SeasonalPerks.Server.Effects;

internal static class TemplateFilters
{
    internal static IEnumerable<string> Ancestors(TemplateTable templates, MongoId id)
    {
        var seen = new HashSet<MongoId>();
        while (
            templates.Items.TryGetValue(id, out var template)
            && template.Parent is { } parent
            && seen.Add(parent)
        )
        {
            yield return parent.ToString();
            id = parent;
        }
    }

    internal static IEnumerable<string> Candidates(TemplateTable templates, PerkEffect effect) =>
        templates
            .Items.Where(pair =>
                pair.Value.Type == "Item"
                && RuntimeEffects.MatchesFilter(
                    effect.ItemFilter,
                    pair.Key.ToString(),
                    Ancestors(templates, pair.Key)
                )
            )
            .Select(pair => pair.Key.ToString())
            .OrderBy(id => id, StringComparer.Ordinal);
}
