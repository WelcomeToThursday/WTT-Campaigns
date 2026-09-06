using SeasonalPerks.Shared.Effects;
using SeasonalPerks.Shared.Perks;
using SeasonalPerks.Shared.Profiles;

namespace SeasonalPerks.Shared.Effects.Consumables;

public static class AllergyEffects
{
    public const string PerkId = "69c3d6a9af28f094100fe128";

    // Keep the gate narrower than the whole allergy family: only the captured
    // category-filtered, six-symptom variant is supported here.
    public static bool Supports(PerkEffect effect)
    {
        if (
            effect.EffectId != "allergy"
            || effect.RandomSlotCount != 3
            || effect.ItemFilter?.Include is not { } include
            || effect.ItemFilter?.Exclude is not { } exclude
            || exclude.Count != 0
            || include.Count != 3
            || include.Any(r => r.Field != "ParentId")
            || !new HashSet<string>(include.Select(r => r.Value)).SetEquals(
                new[] { "5448f3a14bdc2d27728b4569", "5448f3a64bdc2d60728b456a", "543be6674bdc2df1348b4569" }
            )
            || effect.SubEffects is not { } subs
        )
            return false;
        var expected = new Dictionary<string, (float duration, float rate)>
        {
            ["pain"] = (30, 0),
            ["tremor"] = (20, 0),
            ["tunnelVision"] = (20, 0),
            ["healthRegeneration"] = (30, -3),
            ["energyRecovery"] = (30, -3),
            ["hydrationRecovery"] = (30, -3),
        };
        var enabled = subs.Where(p => p.Value.Enabled == true).ToArray();
        return enabled.Length == expected.Count
            && enabled.All(p =>
                expected.TryGetValue(p.Key, out var value)
                && p.Value.DurationSeconds == value.duration
                && (p.Value.Amount ?? 0) == value.rate
            );
    }

    public static T[] Sample<T>(IEnumerable<T> source, int count, Func<int, int> next)
    {
        var pool = source.Distinct().ToArray();
        count = Math.Min(Math.Max(count, 0), pool.Length);
        for (var i = 0; i < count; i++)
        {
            var offset = next(pool.Length - i);
            if (offset < 0 || offset >= pool.Length - i)
                throw new ArgumentOutOfRangeException(nameof(next));
            var j = i + offset;
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }
        return pool.Take(count).ToArray();
    }

    public static void UpdateParameters(
        Catalogue catalogue,
        PerkState state,
        Func<PerkEffect, IEnumerable<string>> candidates,
        Func<int, int> next
    )
    {
        var allergy = state.SeasonalPerkEffectParameters.Allergy ?? new Dictionary<string, AllergyTargets>();
        foreach (var perk in catalogue.All.Where(p => state.SeasonalPerks.Contains(p.Id)))
        {
            var effect = perk.Effects.FirstOrDefault(Supports);
            if (effect == null)
                continue;
            // Keep the original roll even across deselection. Runtime always checks
            // selection; retaining parameters prevents edit-based rerolling.
            if (
                allergy.TryGetValue(perk.Id, out var receipt)
                && receipt?.TargetItems is { } saved
                && saved.Count == 3
                && saved.All(s => !string.IsNullOrEmpty(s))
                && saved.Distinct().Count() == 3
            )
                continue;
            var targets = Sample(candidates(effect), 3, next);
            if (targets.Length != 3)
                throw new InvalidOperationException("Allergic needs at least three compatible item templates.");
            allergy[perk.Id] = new AllergyTargets { TargetItems = targets.ToList() };
        }
        if (allergy.Count > 0)
            state.SeasonalPerkEffectParameters.Allergy = allergy;
    }

    public static IEnumerable<ConsumableEffect> ForItem(RuntimeEffects runtime, string templateId, Func<int, int> next)
    {
        foreach (var perk in runtime.Perks)
        foreach (var symptom in ForPerk(perk, runtime.Parameters, templateId, next))
            yield return symptom;
    }

    internal static IEnumerable<ConsumableEffect> ForPerk(Perk perk, EffectParameters parameters, string templateId, Func<int, int> next)
    {
        if (
            parameters.Allergy == null
            || !parameters.Allergy.TryGetValue(perk.Id, out var receipt)
            || receipt?.TargetItems is not { } targets
            || !targets.Contains(templateId)
        )
            yield break;
        foreach (var effect in perk.Effects.Where(Supports))
        {
            var enabled = effect.SubEffects!.Where(p => p.Value.Enabled == true);
            foreach (var sub in Sample(enabled, 3, next))
                yield return new ConsumableEffect(
                    sub.Key,
                    sub.Value.DurationSeconds!.Value,
                    sub.Value.Amount ?? 0,
                    targets.Select(s => s!).ToArray()
                );
        }
    }
}
