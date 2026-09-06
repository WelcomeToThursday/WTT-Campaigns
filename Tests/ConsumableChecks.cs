using System.Text.Json.Nodes;
using Newtonsoft.Json;
using SeasonalPerks.Shared.Effects;
using SeasonalPerks.Shared.Effects.Consumables;
using SeasonalPerks.Shared.Perks;
using SeasonalPerks.Shared.Profiles;

namespace SeasonalPerks.Tests;

internal static class ConsumableChecks
{
    internal static void Run(Catalogue catalogue, Action<bool, string> check)
    {
        const string juice = "69c406731d8aec4a2b0551bd",
            sailor = "69c40ae21d8aec4a2b0551c2",
            allergic = "69c3d6a9af28f094100fe128";
        var combined = new RuntimeEffects(catalogue, new[] { juice, sailor });
        foreach (var (id, kind, duration, rate) in new[] { (juice, "onPainkillers", 60f, 0f), (sailor, "healthRegeneration", 30f, 2f) })
        {
            var perk = catalogue.All.Single(p => p.Id == id);
            var effect = ConsumableEffects.Describe(perk.Effects.Single())!;
            check(EffectSupport.UnavailableReason(perk) == null, "Consumable perk is selectable: " + id);
            check(
                effect.Kind == kind && effect.Duration.Equals(duration) && effect.Rate.Equals(rate),
                "Captured consumable magnitude and duration: " + id
            );
            check(effect.Targets.Count == 4, "All four fixed targets are available: " + id);
            foreach (var target in effect.Targets)
            {
                var match = ConsumableEffects.ForItem(combined, target).Single();
                check(match.Kind == kind, "Only the matching consumable perk applies: " + target);
                check(
                    !ConsumableEffects.ForItem(new RuntimeEffects(catalogue, Array.Empty<string>()), target).Any(),
                    "Removal restores normal item use: " + target
                );
            }
        }
        check(!ConsumableEffects.ForItem(combined, "5448fee04bdc2dbc018b4567").Any(), "Unlisted water receives no buff");
        check(
            EffectSupport.UnavailableReason(catalogue.All.Single(p => p.Id == allergic)) == null,
            "Allergic is implemented separately from fixed consumable effects"
        );
        check(
            !ConsumableEffects.ForItem(new RuntimeEffects(catalogue, new[] { allergic }), "57347d9c245977448b40fa85").Any(),
            "Allergic requires its persisted target set"
        );
        var modified = JsonConvert.DeserializeObject<PerkEffect>(
            JsonConvert.SerializeObject(catalogue.All.Single(p => p.Id == juice).Effects[0])
        )!;
        modified.RandomSlotCount = 3;
        check(ConsumableEffects.Describe(modified) == null, "Random target subset remains gated");
        modified.RandomSlotCount = 4;
        modified.SubEffects!["pain"].Enabled = true;
        check(ConsumableEffects.Describe(modified) == null, "Multiple enabled sub-effects remain gated");
        modified.SubEffects!["pain"].Enabled = false;
        modified.SubEffects!["onPainkillers"].DurationSeconds = 0;
        check(ConsumableEffects.Describe(modified) == null, "Zero duration remains gated");

        var state = new PerkState
        {
            SeasonalPerks = new() { juice, sailor },
            SeasonalPerkEffectParameters = JsonConvert.DeserializeObject<EffectParameters>(
                "{other:{value:7},allergy:{legacy:{targetItems:['preserved']}}}"
            )!,
        };
        ConsumableEffects.UpdateParameters(catalogue, state);
        foreach (var id in new[] { juice, sailor })
        {
            var expected = ConsumableEffects.Describe(catalogue.All.Single(p => p.Id == id).Effects[0])!.Targets;
            check(
                state.SeasonalPerkEffectParameters.Allergy![id].TargetItems!.SequenceEqual(expected),
                "Persist complete fixed target set: " + id
            );
        }
        var snapshot = JsonNode.Parse(JsonConvert.SerializeObject(state.SeasonalPerkEffectParameters));
        ConsumableEffects.UpdateParameters(catalogue, state);
        check(
            JsonNode.DeepEquals(snapshot, JsonNode.Parse(JsonConvert.SerializeObject(state.SeasonalPerkEffectParameters))),
            "Repeated saves never reroll targets"
        );
        state.SeasonalPerks.Remove(juice);
        ConsumableEffects.UpdateParameters(catalogue, state);
        check(
            !state.SeasonalPerkEffectParameters.Allergy!.ContainsKey(juice)
                && state.SeasonalPerkEffectParameters.Allergy!.ContainsKey(sailor),
            "Removing one perk removes only its parameters"
        );
        check(
            JsonNode.Parse(JsonConvert.SerializeObject(state.SeasonalPerkEffectParameters))!["other"]!["value"]!.GetValue<int>() == 7
                && state.SeasonalPerkEffectParameters.Allergy!["legacy"].TargetItems![0] == "preserved",
            "Unrelated parameters are preserved"
        );
        state.SeasonalPerks.Add(juice);
        ConsumableEffects.UpdateParameters(catalogue, state);
        check(
            JsonNode.DeepEquals(snapshot, JsonNode.Parse(JsonConvert.SerializeObject(state.SeasonalPerkEffectParameters))),
            "Reselection restores identical targets"
        );
        var legacy = new PerkState
        {
            SeasonalPerks = new() { juice },
            SeasonalPerkEffectParameters = JsonConvert.DeserializeObject<EffectParameters>("{allergy:[]}")!,
        };
        ConsumableEffects.UpdateParameters(catalogue, legacy);
        check(
            legacy.SeasonalPerkEffectParameters.Allergy?.ContainsKey(juice) == true,
            "Captured empty-array parameters migrate on selection"
        );

        var receipt = new ConsumptionReceipt();
        check(!receipt.Observe(100, 100, false), "Animation start alone grants nothing");
        check(!receipt.Observe(100, 101, false), "Resource refill grants nothing");
        check(!receipt.Observe(100, 99, true), "Interrupted operation grants nothing");
        check(receipt.Observe(100, 99, false), "First positive consumption grants once");
        check(!receipt.Observe(99, 98, false), "Later use ticks do not extend the timer");
        check(!receipt.Observe(98, 0, false), "Final tick cannot double apply");
        check(!receipt.Observe(98, 97, true), "Later interruption cannot reapply");
        check(new ConsumptionReceipt().Observe(98, 97, false), "A separate partial use can refresh");
        check(new ConsumptionReceipt().Observe(1, 0, false), "A single-use canned food triggers");
        check(!new ConsumptionReceipt().Observe(0, 0, false), "Empty item grants nothing");
        check(!new ConsumptionReceipt().Observe(float.PositiveInfinity, 0, false), "Non-finite resource grants nothing");
        check(!new ConsumptionReceipt().Observe(10, float.NaN, false), "Invalid consumption grants nothing");
        check(new ConsumptionReceipt().Observe(100, 99.5f, false), "Diet's reduced resource use still triggers");
    }
}
