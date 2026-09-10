using WTT.Campaigns.Shared.Perks;
using WTT.Campaigns.Shared.Profiles;

namespace WTT.Campaigns.Shared.Effects.Consumables;

public static class ConsumableEffects
{
    // Fixed-target buffs use this descriptor. AllergyEffects handles persistent
    // random targets and multiple symptoms separately.
    public static ConsumableEffect? Describe(PerkEffect effect)
    {
        if (
            effect.EffectId != "allergy"
            || effect.ItemFilter is not { } filter
            || filter.Include is not { } include
            || include.Count == 0
            || include.Any(r => r.Field != "_tpl" || string.IsNullOrEmpty(r.Value))
            || filter.Exclude is not { } exclude
            || exclude.Count != 0
            || effect.SubEffects is not { } subEffects
        )
        {
            return null;
        }

        // Every included template ID passed the nonempty check above.
        var targets = include.Select(r => r.Value!).Distinct().ToArray();
        if (effect.RandomSlotCount != targets.Length)
        {
            return null;
        }

        var enabled = subEffects.Where(p => p.Value.Enabled == true).ToArray();
        if (enabled.Length != 1)
        {
            return null;
        }

        var sub = enabled[0];
        var duration = sub.Value.DurationSeconds ?? 0;
        var rate = sub.Value.Amount ?? 0;
        if (!PositiveFinite(duration) || (sub.Key != "onPainkillers" && (sub.Key != "healthRegeneration" || !PositiveFinite(rate))))
        {
            return null;
        }

        return new ConsumableEffect(sub.Key, duration, rate, targets);
    }

    public static IEnumerable<ConsumableEffect> ForItem(RuntimeEffects effects, string templateId)
    {
        return effects.Matching("allergy").Select(Describe).Where(e => e != null && e.Targets.Contains(templateId)).Select(e => e!);
    }

    public static void UpdateParameters(Catalogue catalogue, PerkState state)
    {
        var parameters = state.SeasonalPerkEffectParameters;
        var allergy = parameters.Allergy ?? new Dictionary<string, AllergyTargets>();
        foreach (var perk in catalogue.All)
        {
            var effect = perk.Effects.Select(Describe).FirstOrDefault(e => e != null);
            if (effect == null)
            {
                continue;
            }

            if (state.SeasonalPerks.Contains(perk.Id))
            {
                allergy[perk.Id] = new AllergyTargets { TargetItems = effect.Targets.ToList() };
            }
            else
            {
                allergy.Remove(perk.Id);
            }
        }
        if (allergy.Count > 0 || parameters.Allergy != null)
        {
            parameters.Allergy = allergy;
        }
    }

    public static bool PositiveFinite(float value)
    {
        return value > 0 && !float.IsInfinity(value);
    }

    public static IEnumerable<ConsumableEffect> ForUse(RuntimeEffects runtime, string templateId, Func<int, int> next)
    {
        // Preserve catalogue order when two perks refresh the same effect family.
        foreach (var perk in runtime.Perks)
        {
            foreach (var descriptor in perk.Effects.Select(Describe))
            {
                if (descriptor != null && descriptor.Targets.Contains(templateId))
                {
                    yield return descriptor;
                }
            }

            foreach (var symptom in AllergyEffects.ForPerk(perk, runtime.Parameters, templateId, next))
            {
                yield return symptom;
            }
        }
    }
}
