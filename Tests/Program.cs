using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SeasonalPerks.Shared;

if (args.Length == 3 && args[0] == "--resource-hooks")
{
    SeasonalPerks.Tests.ItemResourceHookChecks.Run(args[1], args[2]);
    return;
}

if (args.Length >= 2 && args[0] == "--ui")
{
    SeasonalPerks.Tests.UiCompatibilityChecks.Run(args[1], args.Length > 2 ? args[2] : null);
    return;
}

var c = JsonConvert.DeserializeObject<Catalogue>(
    File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data/catalogue.json"))
)!;
int count = 0;
void Check(bool value, string name)
{
    if (!value)
    {
        throw new Exception(name);
    }
    count++;
}
var rules = new Rules();
var selectedNormal = new Snapshot { ActiveMode = "normal", EffectiveProfileId = "normal-profile" };
Check(
    CharacterSession.IsLoaded(selectedNormal, "normal", "normal-profile"),
    "Already-loaded normal profile can close selection"
);
Check(
    !CharacterSession.IsLoaded(selectedNormal, "normal", "seasonal-profile"),
    "Server mode change alone must not skip client reconnect"
);
Check(
    !CharacterSession.IsLoaded(selectedNormal, "normal", null),
    "Failed or unfinished reconnect remains retryable"
);
Check(
    !CharacterSession.IsLoaded(selectedNormal, "seasonal", "normal-profile"),
    "Opposite character requires reconnect"
);
var selectedSeasonal = new Snapshot
{
    ActiveMode = "seasonal",
    EffectiveProfileId = "seasonal-profile",
};
Check(
    !CharacterSession.IsLoaded(selectedSeasonal, "seasonal", "normal-profile"),
    "Normal-to-seasonal also checks actual loaded identity"
);
var unavailable = new Dictionary<string, string>();
Check(c.Common.Count == 6 && c.Personal.Count == 33, "Captured counts");
Check(c.Personal.Count(p => p.Points < 0) == 19, "Point sign");
foreach (var p in c.Personal)
{
    foreach (var other in p.Conflicts)
    {
        Check(
            c.Personal.Single(o => o.Id == other).Conflicts.Contains(p.Id),
            "Reciprocal exclusions"
        );
    }
}

const string bleed = "69c3cd003ffdba4e68086bd7",
    lessBleed = "69c4116a383fc5c9ad03f3d1";
Check(
    Selection.Validate(c, new[] { bleed }, rules, unavailable) == null,
    "Negative perk grants budget"
);
Check(
    Selection.Validate(c, new[] { lessBleed }, rules, unavailable) != null,
    "Positive perk costs budget"
);
Check(
    Selection.Validate(c, new[] { bleed, lessBleed }, rules, unavailable) != null,
    "Conflict blocked"
);
Check(
    Selection.Validate(c, new[] { bleed, bleed }, rules, unavailable) != null,
    "Duplicate rejected"
);
Check(Selection.Validate(c, new[] { "missing" }, rules, unavailable) != null, "Unknown rejected");
rules.EnforceBudget = false;
Check(Selection.Validate(c, new[] { lessBleed }, rules, unavailable) == null, "Sandbox budget");
Check(
    new RuntimeEffects(c, new[] { bleed }).Multiplier("bleeding_chance_multiplicator") == 1.25f,
    "Captured multiplier"
);
Check(
    new RuntimeEffects(c, Array.Empty<string>()).Multiplier("bleeding_chance_multiplicator") == 1f,
    "Removal restores neutral"
);
var filter = JObject.Parse(
    "{include:[{field:'ParentId',value:'meds'}],exclude:[{field:'_tpl',value:'excluded'}]}"
);
Check(RuntimeEffects.MatchesFilter(filter, "medkit", new[] { "meds" }), "Parent filter");
Check(!RuntimeEffects.MatchesFilter(filter, "excluded", new[] { "meds" }), "Exclusion precedence");
Check(!RuntimeEffects.MatchesFilter(filter, "food", new[] { "provisions" }), "Nonmatching filter");
var youth = new RuntimeEffects(c, new[] { "69c40c0f5e9ce5a8970f6be9" });
Check(youth.Offset("stamina_scale_body_parts", "arms") == 10, "Additive capacity");
Check(youth.Offset("stamina_scale_body_parts", "legs") == 10, "Leg capacity");
var roundTrip = JsonConvert.DeserializeObject<Catalogue>(JsonConvert.SerializeObject(c))!;
Check(
    roundTrip.All.SelectMany(p => p.Effects).Any(e => e["appliedRandomEffectCount"] != null),
    "Preserve additional effect fields"
);
Check(KeyUsage.ConsumptionChance(0.25f) == 0.75f, "Safecracker preserves one quarter of uses");
Check(KeyUsage.ConsumptionChance(2) == 0.5f, "Durability over one uses reciprocal chance");
Check(KeyUsage.ConsumptionChance(0) == 1, "Zero durability multiplier consumes normally");
Check(KeyUsage.ConsumptionChance(-1) == 1, "Negative durability multiplier consumes normally");
Check(KeyUsage.ConsumptionChance(1) == 0, "Unit durability multiplier preserves use");
var resourceEffects = new RuntimeEffects(
    c,
    new[] { "69c3d036042c81ad9209eeae", "69c40f9f9b5263783d0fe51d" }
);
foreach (
    var medical in c.All.Single(p => p.Id == "69c3d036042c81ad9209eeae").Effects[0]["itemFilter"]![
        "include"
    ]!
)
    Check(
        resourceEffects.ItemResourceMultiplier((string)medical["value"]!, Array.Empty<string>())
            == 1.25f,
        "Captured medical inclusion " + medical["value"]
    );
Check(
    resourceEffects.ItemResourceMultiplier("water", new[] { "drink", "543be6674bdc2df1348b4569" })
        == 0.5f,
    "Diet matches provision ancestor"
);
Check(
    resourceEffects.ItemResourceMultiplier("unlisted-medicine", new[] { "medical" }) == 1f,
    "Unlisted medical item stays neutral"
);
Check(
    new RuntimeEffects(c, Array.Empty<string>()).ItemResourceMultiplier(
        "water",
        new[] { "543be6674bdc2df1348b4569" }
    ) == 1f,
    "Removing resource perks restores normal consumption"
);
Check(
    c.All.Count(p => EffectSupport.UnavailableReason(p) == null) == 23,
    "23 implemented catalogue entries"
);
if (args.Length > 0)
{
    SeasonalPerks.Tests.CompatibilityChecks.Run(args[0], Check);
}
Console.WriteLine($"PASS {count} assertions");
