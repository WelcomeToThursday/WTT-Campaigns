using System.Text.Json.Nodes;
using Newtonsoft.Json;
using SeasonalPerks.Shared;

namespace SeasonalPerks.Tests;

internal static class SerializationChecks
{
    internal static void Run(Catalogue catalogue, Action<bool, string> check)
    {
        var captured = JsonNode.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data/catalogue.json"))
        )!;
        var serialized = JsonNode.Parse(JsonConvert.SerializeObject(catalogue))!;
        foreach (var group in new[] { "common", "personal" })
        {
            for (var index = 0; index < captured[group]!.AsArray().Count; index++)
            {
                check(
                    JsonNode.DeepEquals(
                        captured[group]![index]!["effects"],
                        serialized[group]![index]!["effects"]
                    ),
                    "Typed effects preserve every captured field: " + captured[group]![index]!["id"]
                );
            }
        }

        const string futureEffect = """
            {
                "effectId": "future_effect",
                "futureSettings": { "weights": [1, 2.5], "active": true },
                "itemFilter": {
                    "include": [{ "field": "_tpl", "value": "test", "futureRule": [false] }],
                    "futureFilter": { "value": 7 }
                },
                "subEffects": {
                    "futureSymptom": { "enabled": true, "futureAmount": { "range": [3, 4] } }
                }
            }
            """;
        var effect = JsonConvert.DeserializeObject<PerkEffect>(futureEffect)!;
        check(
            JsonNode.DeepEquals(
                JsonNode.Parse(futureEffect),
                JsonNode.Parse(JsonConvert.SerializeObject(effect))
            ),
            "Unknown effect, filter, rule and symptom fields survive serialization"
        );
        check(
            EffectSupport.UnavailableReason(new Perk { Effects = new() { effect } }) != null,
            "Unknown effect families remain unavailable"
        );

        const string savedParameters = """
            {
                "allergy": {
                    "saved-perk": { "targetItems": ["a", "b", "c"], "futureReceipt": [1, 2] }
                },
                "futureFamily": { "settings": [true, { "value": 9 }] }
            }
            """;
        var parameters = JsonConvert.DeserializeObject<EffectParameters>(savedParameters)!;
        var runtime = new RuntimeEffects(catalogue, Array.Empty<string>(), parameters);
        check(
            JsonNode.DeepEquals(
                JsonNode.Parse(savedParameters),
                JsonNode.Parse(JsonConvert.SerializeObject(runtime.Parameters))
            ),
            "Runtime parameter copies preserve known and future saved fields"
        );
        parameters.Allergy!["saved-perk"].TargetItems[0] = "changed";
        parameters.Allergy.Clear();
        check(
            runtime
                .Parameters.Allergy!["saved-perk"]
                .TargetItems.SequenceEqual(new[] { "a", "b", "c" }),
            "Runtime parameters own independent maps and target lists"
        );
        var incomplete = JsonConvert.DeserializeObject<EffectParameters>(
            "{allergy:{empty:null,missingTargets:{targetItems:null}}}"
        )!;
        check(
            JsonConvert.SerializeObject(incomplete.DeepClone())
                == JsonConvert.SerializeObject(incomplete),
            "Incomplete legacy receipts can still be copied before target repair"
        );
        foreach (var multiplier in new[] { 0d, -1d, double.NaN })
        {
            var resource = new PerkEffect
            {
                EffectId = "item_resource_drain_multiplicator",
                Multiplier = multiplier,
            };
            var restored = JsonConvert.DeserializeObject<PerkEffect>(
                JsonConvert.SerializeObject(resource)
            )!;
            var resources = new RuntimeEffects(
                new Catalogue
                {
                    Personal = new()
                    {
                        new()
                        {
                            Id = "resource",
                            Effects = new() { restored },
                        },
                    },
                },
                new[] { "resource" }
            );
            check(
                resources.ItemResourceMultiplier("any", Array.Empty<string>()) == 1,
                "Nonpositive and NaN resource multipliers remain neutral: " + multiplier
            );
        }
        check(
            RuntimeEffects.MatchesFilter(null, "any", Array.Empty<string>())
                && RuntimeEffects.MatchesFilter(new ItemFilter(), "any", Array.Empty<string>())
                && new RuntimeEffects(
                    new Catalogue
                    {
                        Personal = new()
                        {
                            new()
                            {
                                Id = "missing",
                                Effects = new() { new() { EffectId = "test" } },
                            },
                        },
                    },
                    new[] { "missing" }
                ).Multiplier("test") == 1,
            "Absent optional filter and multiplier fields retain their neutral defaults"
        );
    }
}
