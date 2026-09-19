using WTT.Campaigns.Shared.Perks;
using WTT.Campaigns.Shared.Profiles;

namespace WTT.Campaigns.Shared.Effects;

/// <summary>Immutable aggregate rebuilt from templates, never from already modified player values.</summary>
public sealed class RuntimeEffects
{
    public IReadOnlyList<PerkEffect> Effects { get; }
    public IReadOnlyList<Perk> Perks { get; }
    public EffectParameters Parameters { get; }

    public RuntimeEffects(Catalogue catalogue, IEnumerable<string> selected, EffectParameters? parameters = null)
    {
        var ids = new HashSet<string>(selected);
        Perks = catalogue.All.AsValueEnumerable().Where(p => ids.Contains(p.Id)).ToArray();
        Effects = Perks.AsValueEnumerable().SelectMany(p => p.Effects).ToArray();
        Parameters = parameters?.DeepClone() ?? new EffectParameters();
    }

    public bool Has(string id)
    {
        return Effects.AsValueEnumerable().Any(e => e.EffectId == id);
    }

    public bool HideoutRequiresFir(bool original)
    {
        return original && !Matching("hideout_fir").AsValueEnumerable().Any(e => e.Mode == "not_require");
    }

    // Native TryApply maps primary to slowdown and secondary to noise.
    public float BushSlowdownMultiplier
    {
        get { return BushMultiplier(effect => effect.PrimaryMultiplier); }
    }

    public float BushNoiseMultiplier
    {
        get { return BushMultiplier(effect => effect.SecondaryMultiplier); }
    }

    private float BushMultiplier(Func<PerkEffect, float?> multiplier)
    {
        return Matching("bush_interaction_multiplicators")
            .AsValueEnumerable()
            .Aggregate(1f, (value, effect) => value * (multiplier(effect) ?? 1f));
    }

    public float Multiplier(string id, string? body = null)
    {
        return Matching(id)
            .AsValueEnumerable()
            .Where(e => body == null || Contains(e.BodyPartTypes, body))
            .Aggregate(1f, (v, e) => v * ((float?)e.Multiplier ?? 1f));
    }

    public int Offset(string id, string body)
    {
        return Matching(id).AsValueEnumerable().Where(e => Contains(e.BodyPartTypes, body)).Sum(e => e.IntValue ?? 0);
    }

    public float SkillMultiplier(string skill)
    {
        return Matching("skill_experience_multiplicator")
            .AsValueEnumerable()
            .Where(e => Contains(e.SkillIds, skill))
            .Aggregate(1f, (v, e) => v * ((float?)e.Multiplier ?? 1));
    }

    public int SkillCap(string skill)
    {
        return Matching("skill_max_level_cap")
            .AsValueEnumerable()
            .Where(e => Contains(e.SkillIds, skill))
            .Select(e => e.IntValue ?? 51)
            .DefaultIfEmpty(51)
            .Min();
    }

    public bool SkillBlocked(string skill)
    {
        return Matching("skill_not_growing").AsValueEnumerable().Any(e => Contains(e.SkillIds, skill));
    }

    public decimal TraderMultiplier(string trader, string action)
    {
        return Matching("trader_prices_by_trader_multiplicator")
            .AsValueEnumerable()
            .Where(e => Contains(e.TraderIds, trader) && e.TradeAction == action)
            .Aggregate(1m, (v, e) => v * ((decimal?)e.Multiplier ?? 1m));
    }

    public float ItemResourceMultiplier(string templateId, IEnumerable<string> ancestors)
    {
        var parents = ancestors.AsValueEnumerable().ToArray();
        return Matching("item_resource_drain_multiplicator")
            .AsValueEnumerable()
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

    public IEnumerable<PerkEffect> Matching(string id)
    {
        foreach (var effect in Effects)
            if (effect.EffectId == id)
                yield return effect;
    }

    public static bool Contains(IEnumerable<string>? values, string value)
    {
        return values?.AsValueEnumerable().Contains(value) ?? false;
    }

    public static bool MatchesFilter(ItemFilter? filter, string templateId, IEnumerable<string> ancestors)
    {
        if (filter == null)
        {
            return true;
        }

        var parents = new HashSet<string>(ancestors);
        bool Match(ItemFilterRule rule)
        {
            return rule.Field switch
            {
                "_tpl" => rule.Value == templateId,
                "ParentId" => parents.Contains(rule.Value ?? ""),
                _ => false,
            };
        }

        var include = filter.Include;
        var exclude = filter.Exclude;
        return (include == null || include.Count == 0 || include.AsValueEnumerable().Any(Match))
            && !(exclude?.AsValueEnumerable().Any(Match) ?? false);
    }
}
