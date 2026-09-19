namespace WTT.Campaigns.Shared.Effects;

public static class EffectParametersValidator
{
    public static string? Error(PerkEffect effect)
    {
        foreach (var n in new double?[] { effect.Multiplier, effect.PrimaryMultiplier, effect.SecondaryMultiplier })
        {
            if (n.HasValue && (double.IsNaN(n.Value) || double.IsInfinity(n.Value) || n < 0 || n > 100))
            {
                return "Effect multipliers must be finite, between zero and 100.";
            }
        }

        var id = effect.EffectId ?? "";
        if (id.EndsWith("multiplicator", StringComparison.Ordinal) && effect.Multiplier == null)
        {
            return "This effect requires a multiplier.";
        }

        if (id.StartsWith("skill_", StringComparison.Ordinal) && effect.SkillIds is not { Count: > 0 })
        {
            return "Choose at least one skill.";
        }

        if (id is "skill_level_preset" or "skill_max_level_cap" && effect.IntValue is not (>= 0 and <= 51))
        {
            return "Skill levels must be 0–51.";
        }

        if (id.Contains("body_parts") && effect.BodyPartTypes is not { Count: > 0 })
        {
            return "Choose affected body parts.";
        }

        if (
            id == "trader_prices_by_trader_multiplicator"
            && (effect.TraderIds is not { Count: > 0 } || effect.TradeAction is not ("buy" or "sell"))
        )
        {
            return "Choose traders and buy/sell direction.";
        }

        if (
            effect.ItemFilter != null
            && (effect.ItemFilter.Include ?? new())
                .AsValueEnumerable()
                .Concat(effect.ItemFilter.Exclude ?? new())
                .Any(f => f.Field is not ("_tpl" or "ParentId") || string.IsNullOrEmpty(f.Value))
        )
        {
            return "Item filters require a template or parent category.";
        }

        return null;
    }
}
