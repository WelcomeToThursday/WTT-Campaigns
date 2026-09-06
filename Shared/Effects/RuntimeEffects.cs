using SeasonalPerks.Shared.Perks;
using SeasonalPerks.Shared.Profiles;

namespace SeasonalPerks.Shared.Effects;

/// <summary>Immutable aggregate rebuilt from templates, never from already modified player values.</summary>
public sealed class RuntimeEffects
{
    public IReadOnlyList<PerkEffect> Effects { get; }
    public IReadOnlyList<Perk> Perks { get; }
    public EffectParameters Parameters { get; }

    public RuntimeEffects(
        Catalogue catalogue,
        IEnumerable<string> selected,
        EffectParameters? parameters = null
    )
    {
        var ids = new HashSet<string>(selected);
        Perks = catalogue.All.Where(p => ids.Contains(p.Id)).ToArray();
        Effects = Perks.SelectMany(p => p.Effects).ToArray();
        Parameters = parameters?.DeepClone() ?? new EffectParameters();
    }

    public bool Has(string id) => Effects.Any(e => e.EffectId == id);

    public bool HideoutRequiresFir(bool original) =>
        original && !Matching("hideout_fir").Any(e => e.Mode == "not_require");

    // Native TryApply maps primary to slowdown and secondary to noise.
    public float BushSlowdownMultiplier => BushMultiplier(effect => effect.PrimaryMultiplier);
    public float BushNoiseMultiplier => BushMultiplier(effect => effect.SecondaryMultiplier);

    private float BushMultiplier(Func<PerkEffect, float?> multiplier) =>
        Matching("bush_interaction_multiplicators")
            .Aggregate(1f, (value, effect) => value * (multiplier(effect) ?? 1f));

    public float Multiplier(string id, string? body = null) =>
        Matching(id)
            .Where(e => body == null || Contains(e.BodyPartTypes, body))
            .Aggregate(1f, (v, e) => v * ((float?)e.Multiplier ?? 1f));

    public int Offset(string id, string body) =>
        Matching(id).Where(e => Contains(e.BodyPartTypes, body)).Sum(e => e.IntValue ?? 0);

    public float SkillMultiplier(string skill) =>
        Matching("skill_experience_multiplicator")
            .Where(e => Contains(e.SkillIds, skill))
            .Aggregate(1f, (v, e) => v * ((float?)e.Multiplier ?? 1));

    public int SkillCap(string skill) =>
        Matching("skill_max_level_cap")
            .Where(e => Contains(e.SkillIds, skill))
            .Select(e => e.IntValue ?? 51)
            .DefaultIfEmpty(51)
            .Min();

    public bool SkillBlocked(string skill) =>
        Matching("skill_not_growing").Any(e => Contains(e.SkillIds, skill));

    public decimal TraderMultiplier(string trader, string action) =>
        Matching("trader_prices_by_trader_multiplicator")
            .Where(e => Contains(e.TraderIds, trader) && e.TradeAction == action)
            .Aggregate(1m, (v, e) => v * ((decimal?)e.Multiplier ?? 1m));

    public float ItemResourceMultiplier(string templateId, IEnumerable<string> ancestors)
    {
        var parents = ancestors.ToArray();
        return Matching("item_resource_drain_multiplicator")
            .Where(e => MatchesFilter(e.ItemFilter, templateId, parents))
            .Aggregate(
                1f,
                (value, effect) =>
                {
                    var multiplier = (float?)effect.Multiplier ?? 1f;
                    // Native PerkRuntimeUtility.PositiveMultiplier treats nonpositive/NaN as neutral.
                    return value * (multiplier > 0f ? multiplier : 1f);
                }
            );
    }

    public IEnumerable<PerkEffect> Matching(string id) => Effects.Where(e => e.EffectId == id);

    public static bool Contains(IEnumerable<string>? values, string value) =>
        values?.Contains(value) ?? false;

    public static bool MatchesFilter(
        ItemFilter? filter,
        string templateId,
        IEnumerable<string> ancestors
    )
    {
        if (filter == null)
        {
            return true;
        }

        var parents = new HashSet<string>(ancestors);
        bool Match(ItemFilterRule rule) =>
            rule.Field switch
            {
                "_tpl" => rule.Value == templateId,
                "ParentId" => parents.Contains(rule.Value ?? ""),
                _ => false,
            };
        var include = filter.Include;
        var exclude = filter.Exclude;
        return (include == null || include.Count == 0 || include.Any(Match))
            && !(exclude?.Any(Match) ?? false);
    }
}
