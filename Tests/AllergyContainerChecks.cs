using Newtonsoft.Json;
using SeasonalPerks.Shared.Effects;
using SeasonalPerks.Shared.Effects.Consumables;
using SeasonalPerks.Shared.Effects.Items;
using SeasonalPerks.Shared.Perks;
using SeasonalPerks.Shared.Profiles;

namespace SeasonalPerks.Tests;

internal static class AllergyContainerChecks
{
    internal static void Run(Catalogue catalogue, Action<bool, string> check)
    {
        var effect = catalogue.All.Single(p => p.Id == AllergyEffects.PerkId).Effects.Single();
        check(AllergyEffects.Supports(effect), "Captured Allergic is supported");
        var state = new PerkState { SeasonalPerks = new() { AllergyEffects.PerkId } };
        var candidates = Enumerable.Range(0, 15).Select(i => "item" + i).ToArray();
        var random = new Random(947);
        AllergyEffects.UpdateParameters(catalogue, state, _ => candidates, random.Next);
        var targets = state.SeasonalPerkEffectParameters.Allergy![AllergyEffects.PerkId].TargetItems.ToArray();
        check(
            targets.Length == 3 && targets.Distinct().Count() == 3 && targets.All(t => candidates.Contains(t)),
            "Three distinct targets are drawn from the eligible pool"
        );
        var saved = JsonConvert.SerializeObject(state);
        state = JsonConvert.DeserializeObject<PerkState>(saved)!;
        AllergyEffects.UpdateParameters(catalogue, state, _ => throw new Exception("Rerolled"), _ => throw new Exception("Rerolled"));
        check(JsonConvert.SerializeObject(state) == saved, "Serialized target receipt is stable without generating again");
        var runtime = new RuntimeEffects(catalogue, state.SeasonalPerks, state.SeasonalPerkEffectParameters);
        foreach (var target in targets)
        {
            var samples = Enumerable.Range(0, 200).Select(_ => AllergyEffects.ForItem(runtime, target!, random.Next).ToArray()).ToArray();
            check(
                samples.All(s => s.Length == 3 && s.Select(e => e.Kind).Distinct().Count() == 3),
                "Exactly three distinct symptoms per use: " + target
            );
            check(samples.SelectMany(s => s).Select(e => e.Kind).Distinct().Count() == 6, "All six symptoms can be chosen: " + target);
            check(
                samples
                    .SelectMany(s => s)
                    .All(e =>
                        e.Kind switch
                        {
                            "pain" => e.Duration == 30 && e.Rate == 0,
                            "tremor" or "tunnelVision" => e.Duration == 20 && e.Rate == 0,
                            _ => e.Duration == 30 && e.Rate == -3,
                        }
                    ),
                "Captured symptom timings and negative rates: " + target
            );
        }
        check(!AllergyEffects.ForItem(runtime, "unlisted", random.Next).Any(), "Unlisted consumables do not roll");
        var fish = "57347d5f245977448b40fa81";
        var combinedParameters = new EffectParameters
        {
            Allergy = new()
            {
                [AllergyEffects.PerkId] = new()
                {
                    TargetItems = new() { fish, "a", "b" },
                },
            },
        };
        var combined = new RuntimeEffects(catalogue, new[] { AllergyEffects.PerkId, "69c40ae21d8aec4a2b0551c2" }, combinedParameters);
        var combinedUse = ConsumableEffects.ForUse(combined, fish, n => n == 6 ? 3 : 0).ToArray();
        check(
            combinedUse.Length == 4 && combinedUse.Last().Kind == "healthRegeneration" && combinedUse.Last().Rate == 2,
            "Overlapping allergy and canned-fish effects retain catalogue application order"
        );
        check(
            combinedUse.Take(3).Any(e => e.Kind == "healthRegeneration" && e.Rate == -3),
            "Negative and positive rate descriptors remain distinct before refresh"
        );
        check(
            !AllergyEffects.ForItem(new RuntimeEffects(catalogue, state.SeasonalPerks), targets[0]!, random.Next).Any(),
            "Missing parameters do not cause client rerolls"
        );
        state.SeasonalPerks.Clear();
        AllergyEffects.UpdateParameters(catalogue, state, _ => candidates, random.Next);
        check(
            !AllergyEffects
                .ForItem(new RuntimeEffects(catalogue, state.SeasonalPerks, state.SeasonalPerkEffectParameters), targets[0]!, random.Next)
                .Any(),
            "Removed allergy does not trigger despite retained receipt"
        );
        state.SeasonalPerks.Add(AllergyEffects.PerkId);
        AllergyEffects.UpdateParameters(catalogue, state, _ => throw new Exception("Rerolled"), random.Next);
        check(JsonConvert.SerializeObject(state) == saved, "Reselection cannot reroll targets");
        var altered = JsonConvert.DeserializeObject<PerkEffect>(JsonConvert.SerializeObject(effect))!;
        altered.RandomSlotCount = 7;
        check(!AllergyEffects.Supports(altered), "Unsupported randomized shapes stay gated");
        check(
            AllergyEffects.Sample(new[] { 1, 1, 2 }, 9, _ => 0).SequenceEqual(new[] { 1, 2 }),
            "Sampling bounds and duplicate pool handling"
        );
        var restriction = new RuntimeEffects(catalogue, new[] { "69c3da13eaf97663fb0bb36d" });
        check(SecureContainerRules.Allows(restriction, "key", new[] { "543be5e94bdc2df1348b4568" }), "Keys allowed by ancestry");
        check(
            SecureContainerRules.Allows(restriction, "special", new[] { "5447e0e74bdc2d3c308b4567" }),
            "Special equipment allowed by ancestry"
        );
        check(
            SecureContainerRules.Allows(restriction, "5449016a4bdc2d6f028b456f", new[] { "543be5dd4bdc2deb348b4569" }),
            "Captured money category allowed"
        );
        check(
            !SecureContainerRules.Allows(restriction, "544fb45d4bdc2dee738b4568", new[] { "5448f39d4bdc2d0a728b4568" }),
            "Salewa rejected"
        );
        check(!SecureContainerRules.Allows(restriction, "unknown", Array.Empty<string>()), "Unknown items cannot bypass the allow-list");
        check(
            SecureContainerRules.Allows(new RuntimeEffects(catalogue, Array.Empty<string>()), "anything", Array.Empty<string>()),
            "Normal mode has no additional restriction"
        );
    }
}
