using SeasonalPerks.Shared.Effects.Consumables;
using SeasonalPerks.Shared.Perks;

namespace SeasonalPerks.Shared.Effects;

public static class EffectSupport
{
    // An entry is selectable only when EVERY effect family has a compatible implementation.
    public static readonly HashSet<string> Implemented = new()
    {
        "skill_level_preset",
        "skill_experience_multiplicator",
        "skill_max_level_cap",
        "skill_not_growing",
        "hydration_drain_multiplicator",
        "energy_drain_multiplicator",
        "stamina_scale_body_parts",
        "stamina_restore_body_parts_multiplicator",
        "craft_time_multiplicator",
        "insurance_disabled",
        "bleeding_chance_multiplicator",
        "fracture_chance_multiplicator",
        "fall_damage_multiplicator",
        "sprint_speed_multiplicator",
        "stamina_consumption_body_parts_multiplicator",
        "key_durability_multiplicator",
        "fresh_wounds_until_raid_end",
        "item_resource_drain_multiplicator",
        "bush_interaction_multiplicators",
        "hideout_fir",
        "trader_prices_by_trader_multiplicator",
        "flea_market_npc_only",
        "pmc_experience_multiplicator",
        "pouch_item_filter_restrict",
    };

    public static string? UnavailableReason(Perk perk)
    {
        if (perk.Effects.Count == 0)
        {
            return "World-rule behavior has not been verified for this SPT version.";
        }

        foreach (var effect in perk.Effects)
        {
            var id = effect.EffectId ?? "unknown";
            if (
                Implemented.Contains(id)
                || ConsumableEffects.Describe(effect) != null
                || AllergyEffects.Supports(effect)
            )
            {
                continue;
            }

            return id switch
            {
                "scheduled_mail" =>
                    "Reward contents and first delivery timing have not been verified.",
                "lucky" or "unlucky" => "Luck behavior has not been verified.",
                "allergy" =>
                    "Item-triggered effects and random sub-effect selection are not yet available.",
                "key_durability_multiplicator" =>
                    "Key consumption behavior is still being verified.",
                _ => "This modifier's gameplay integration is not yet available.",
            };
        }

        return null;
    }
}
