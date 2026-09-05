using Newtonsoft.Json.Linq;

namespace SeasonalPerks.Shared;

/// <summary>Immutable aggregate rebuilt from templates, never from already modified player values.</summary>
public sealed class RuntimeEffects
{
    public IReadOnlyList<JObject> Effects { get; }

    public RuntimeEffects(Catalogue catalogue, IEnumerable<string> selected)
    {
        var ids = new HashSet<string>(selected);
        Effects = catalogue.All.Where(p => ids.Contains(p.Id)).SelectMany(p => p.Effects).ToArray();
    }

    public bool Has(string id) => Effects.Any(e => (string?)e["effectId"] == id);

    public float Multiplier(string id, string? body = null) =>
        Matching(id)
            .Where(e => body == null || Contains(e["bodyPartTypes"], body))
            .Aggregate(1f, (v, e) => v * ((float?)e["multiplicator"] ?? 1f));

    public int Offset(string id, string body) =>
        Matching(id)
            .Where(e => Contains(e["bodyPartTypes"], body))
            .Sum(e => (int?)e["intValue"] ?? 0);

    public float SkillMultiplier(string skill) =>
        Matching("skill_experience_multiplicator")
            .Where(e => Contains(e["skillIds"], skill))
            .Aggregate(1f, (v, e) => v * ((float?)e["multiplicator"] ?? 1));

    public int SkillCap(string skill) =>
        Matching("skill_max_level_cap")
            .Where(e => Contains(e["skillIds"], skill))
            .Select(e => (int?)e["intValue"] ?? 51)
            .DefaultIfEmpty(51)
            .Min();

    public bool SkillBlocked(string skill) =>
        Matching("skill_not_growing").Any(e => Contains(e["skillIds"], skill));

    public float TraderMultiplier(string trader, string action) =>
        Matching("trader_prices_by_trader_multiplicator")
            .Where(e => Contains(e["traderIds"], trader) && (string?)e["tradeAction"] == action)
            .Aggregate(1f, (v, e) => v * ((float?)e["multiplicator"] ?? 1));

    public float ItemResourceMultiplier(string templateId, IEnumerable<string> ancestors)
    {
        var parents = ancestors.ToArray();
        return Matching("item_resource_drain_multiplicator")
            .Where(e => MatchesFilter(e["itemFilter"], templateId, parents))
            .Aggregate(
                1f,
                (value, effect) =>
                {
                    var multiplier = (float?)effect["multiplicator"] ?? 1f;
                    // Native PerkRuntimeUtility.PositiveMultiplier treats nonpositive/NaN as neutral.
                    return value * (multiplier > 0f ? multiplier : 1f);
                }
            );
    }

    public IEnumerable<JObject> Matching(string id) =>
        Effects.Where(e => (string?)e["effectId"] == id);

    public static bool Contains(JToken? token, string value) =>
        token is JArray a && a.Values<string>().Contains(value);

    public static bool MatchesFilter(
        JToken? filter,
        string templateId,
        IEnumerable<string> ancestors
    )
    {
        if (filter is not JObject f)
        {
            return true;
        }

        var parents = new HashSet<string>(ancestors);
        bool Match(JToken rule) =>
            (string?)rule["field"] switch
            {
                "_tpl" => (string?)rule["value"] == templateId,
                "ParentId" => parents.Contains((string?)rule["value"] ?? ""),
                _ => false,
            };
        var include = f["include"] as JArray;
        var exclude = f["exclude"] as JArray;
        return (include == null || include.Count == 0 || include.Any(Match))
            && !(exclude?.Any(Match) ?? false);
    }
}
