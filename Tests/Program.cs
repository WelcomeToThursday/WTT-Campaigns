using Newtonsoft.Json;
using WTT.Campaigns.Shared.Configuration;
using WTT.Campaigns.Shared.Contracts;
using WTT.Campaigns.Shared.Effects;
using WTT.Campaigns.Shared.Effects.Items;
using WTT.Campaigns.Shared.Effects.Movement;
using WTT.Campaigns.Shared.Effects.Skills;
using WTT.Campaigns.Shared.Effects.Trading;
using WTT.Campaigns.Shared.Perks;
using WTT.Campaigns.Shared.Profiles;

if (args.Length == 3 && args[0] == "--mod-identity")
{
    WTT.Campaigns.Tests.ModIdentityChecks.Run(args[1], args[2]);
    return;
}

if (args.Length == 4 && args[0] == "--story-pack")
{
    WTT.Campaigns.Tests.StoryPackTool.Compose(args[1], args[2], args[3]);
    return;
}

if (args.Length == 3 && args[0] == "--test-story-season")
{
    WTT.Campaigns.Tests.PlayableStorySeason.Build(args[1], args[2]);
    return;
}

if (args.Length == 2 && args[0] is "--creator-fixture" or "--story-fixture")
{
    WTT.Campaigns.Tests.CreatorFixture.Prepare(args[1], args[0] == "--story-fixture");
    return;
}

if (args.Length == 3 && (args[0] == "--resource-hooks" || args[0] == "--bush-hooks" || args[0] == "--experience-hooks"))
{
    WTT.Campaigns.Tests.ClientHookChecks.Run(args[1], args[2], args[0] == "--bush-hooks", args[0] == "--experience-hooks");
    return;
}

if (args.Length == 3 && args[0] == "--unity-toolkit")
{
    WTT.Campaigns.Tests.UnityToolkitChecks.Run(args[1], args[2]);
    return;
}

if (args.Length >= 2 && args[0] == "--ui")
{
    WTT.Campaigns.Tests.UiCompatibilityChecks.Run(args[1], args.Length > 2 ? args[2] : null);
    return;
}

var c = JsonConvert.DeserializeObject<Catalogue>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data/catalogue.json")))!;
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
WTT.Campaigns.Tests.CampaignTextChecks.Run(Check);
WTT.Campaigns.Tests.RequestIdentityChecks.Run(Check);
WTT.Campaigns.Tests.ImageRequestCacheChecks.Run(Check).GetAwaiter().GetResult();
WTT.Campaigns.Tests.NativeModelChecks.Run(Check);
WTT.Campaigns.Tests.SeasonItemBundleChecks.Run(Check);
WTT.Campaigns.Tests.StoryChecks.Run(Check);
WTT.Campaigns.Tests.StoryChapterNotificationChecks.Run(Check);
WTT.Campaigns.Tests.StoryEngineChecks.Run(Check);
WTT.Campaigns.Tests.StoryV2Checks.Run(Check);
WTT.Campaigns.Tests.AuthoringChecks.Run(Check);
WTT.Campaigns.Tests.EditorLayoutChecks.Run(Check);
WTT.Campaigns.Tests.ProgressionChecks.Run(Check);
if (args.Length > 0 && File.Exists(args[0]))
{
    WTT.Campaigns.Tests.ProgressionChecks.Hooks(args[0], Check);
}
WTT.Campaigns.Tests.CreatorChecks.Run(Check);
WTT.Campaigns.Tests.HubGameplayChecks.Run(Check);
WTT.Campaigns.Tests.HubDocumentLootChecks.Run(Check);
var seasoned = new RuntimeEffects(c, new[] { "69c41adf883efd5e3b09ccae" });
Check(seasoned.Multiplier("pmc_experience_multiplicator").Equals(1.25f), "Captured PMC experience bonus");
Check(ExperienceScaling.Award(1700, 1.25f) == 2125, "Quest XP gains 25 percent");
Check(ExperienceScaling.Award(3, 1.25f) == 3, "Integer XP truncates fractional remainder");
Check(ExperienceScaling.Total(10000, 10100, 1.25f) == 10125, "Only newly awarded XP is multiplied");
Check(ExperienceScaling.Total(10000, 10000, 1.25f) == 10000, "Unchanged XP does not compound");
Check(ExperienceScaling.Total(10000, 9000, 1.25f) == 9000, "XP reductions stay unchanged");
Check(ExperienceScaling.Total(int.MaxValue - 10, int.MaxValue, 1.25f) == int.MaxValue, "Bonus cannot overflow profile XP");
Check(ExperienceScaling.Award(100, 1f) == 100, "Normal PMC award stays neutral");
Check(new RuntimeEffects(c, new[] { "69c3da8fc0e4deb02605f3c9" }).Has("flea_market_npc_only"), "Captured trader-only restriction");
var selectedNormal = new Snapshot { ActiveMode = "normal", EffectiveProfileId = "normal-profile" };
Check(CharacterSession.IsLoaded(selectedNormal, "normal", "normal-profile"), "Already-loaded normal profile can close selection");
Check(!CharacterSession.IsLoaded(selectedNormal, "normal", "seasonal-profile"), "Server mode change alone must not skip client reconnect");
Check(!CharacterSession.IsLoaded(selectedNormal, "normal", null), "Failed or unfinished reconnect remains retryable");
Check(!CharacterSession.IsLoaded(selectedNormal, "seasonal", "normal-profile"), "Opposite character requires reconnect");
var selectedSeasonal = new Snapshot { ActiveMode = "seasonal", EffectiveProfileId = "seasonal-profile" };
Check(!CharacterSession.IsLoaded(selectedSeasonal, "seasonal", "normal-profile"), "Normal-to-campaign also checks actual loaded identity");
IReadOnlyDictionary<string, string> unavailable = new Dictionary<string, string>();
Check(c.Common.Count == 6 && c.Personal.Count == 33, "Captured counts");
Check(c.Personal.Count(p => p.Points < 0) == 19, "Point sign");
foreach (var p in c.Personal)
{
    foreach (var other in p.Conflicts)
    {
        Check(c.Personal.Single(o => o.Id == other).Conflicts.Contains(p.Id), "Reciprocal exclusions");
    }
}

const string bleed = "69c3cd003ffdba4e68086bd7",
    lessBleed = "69c4116a383fc5c9ad03f3d1";
Check(Selection.Validate(c, new[] { bleed }, rules, unavailable) == null, "Negative perk grants budget");
Check(Selection.Validate(c, new[] { lessBleed }, rules, unavailable) != null, "Positive perk costs budget");
Check(Selection.Validate(c, new[] { bleed, lessBleed }, rules, unavailable) != null, "Conflict blocked");
Check(Selection.Validate(c, new[] { bleed, bleed }, rules, unavailable) != null, "Duplicate rejected");
Check(Selection.Validate(c, new[] { "missing" }, rules, unavailable) != null, "Unknown rejected");
rules.EnforceBudget = false;
Check(Selection.Validate(c, new[] { lessBleed }, rules, unavailable) == null, "Sandbox budget");
Check(new RuntimeEffects(c, new[] { bleed }).Multiplier("bleeding_chance_multiplicator").Equals(1.25f), "Captured multiplier");
Check(new RuntimeEffects(c, Array.Empty<string>()).Multiplier("bleeding_chance_multiplicator").Equals(1f), "Removal restores neutral");
var filter = new ItemFilter
{
    Include = new()
    {
        new() { Field = "ParentId", Value = "meds" },
    },
    Exclude = new()
    {
        new() { Field = "_tpl", Value = "excluded" },
    },
};
Check(RuntimeEffects.MatchesFilter(filter, "medkit", new[] { "meds" }), "Parent filter");
Check(!RuntimeEffects.MatchesFilter(filter, "excluded", new[] { "meds" }), "Exclusion precedence");
Check(!RuntimeEffects.MatchesFilter(filter, "food", new[] { "provisions" }), "Nonmatching filter");
var youth = new RuntimeEffects(c, new[] { "69c40c0f5e9ce5a8970f6be9" });
Check(youth.Offset("stamina_scale_body_parts", "arms") == 10, "Additive capacity");
Check(youth.Offset("stamina_scale_body_parts", "legs") == 10, "Leg capacity");
var roundTrip = JsonConvert.DeserializeObject<Catalogue>(JsonConvert.SerializeObject(c))!;
Check(roundTrip.All.SelectMany(p => p.Effects).Any(e => e.AppliedRandomEffectCount != null), "Preserve additional effect fields");
Check(KeyUsage.ConsumptionChance(0.25f).Equals(0.75f), "Safecracker preserves one quarter of uses");
Check(KeyUsage.ConsumptionChance(2).Equals(0.5f), "Durability over one uses reciprocal chance");
Check(KeyUsage.ConsumptionChance(0).Equals(1), "Zero durability multiplier consumes normally");
Check(KeyUsage.ConsumptionChance(-1).Equals(1), "Negative durability multiplier consumes normally");
Check(KeyUsage.ConsumptionChance(1) == 0, "Unit durability multiplier preserves use");
var resourceEffects = new RuntimeEffects(c, new[] { "69c3d036042c81ad9209eeae", "69c40f9f9b5263783d0fe51d" });
foreach (var medical in c.All.Single(p => p.Id == "69c3d036042c81ad9209eeae").Effects[0].ItemFilter!.Include!)
{
    Check(
        resourceEffects.ItemResourceMultiplier(medical.Value!, Array.Empty<string>()).Equals(1.25f),
        "Captured medical inclusion " + medical.Value
    );
}

Check(
    resourceEffects.ItemResourceMultiplier("water", new[] { "drink", "543be6674bdc2df1348b4569" }).Equals(0.5f),
    "Diet matches provision ancestor"
);
Check(resourceEffects.ItemResourceMultiplier("unlisted-medicine", new[] { "medical" }).Equals(1f), "Unlisted medical item stays neutral");
Check(
    new RuntimeEffects(c, Array.Empty<string>()).ItemResourceMultiplier("water", new[] { "543be6674bdc2df1348b4569" }).Equals(1f),
    "Removing resource perks restores normal consumption"
);
Check(c.All.Count(p => EffectSupport.UnavailableReason(p) == null) == 33, "33 implemented catalogue entries");
var bush = new RuntimeEffects(c, new[] { "69c405a9d7a7b2ca660e0c56" });
const string therapist = "54cb57776803fa99248b456e";
var vacuum = new RuntimeEffects(c, new[] { "69c3d43030f896ebef0ed357" });
var thirdLeg = new RuntimeEffects(c, new[] { "69ce62886e199f4bbe0ab19b" });
Check(vacuum.TraderMultiplier(therapist, "buy") == 1.2m, "Personality Vacuum increases purchases by 20 percent");
Check(vacuum.SkillBlocked("Charisma") && !vacuum.SkillBlocked("Strength"), "Personality Vacuum blocks only Charisma growth");
Check(thirdLeg.TraderMultiplier(therapist, "buy") == 0.95m, "Third Leg discounts Therapist purchases");
Check(thirdLeg.Multiplier("sprint_speed_multiplicator").Equals(0.99f), "Third Leg preserves captured sprint penalty");
Check(thirdLeg.TraderMultiplier("54cb50c76803fa8b248b4571", "buy") == 1m, "Third Leg leaves other traders alone");
Check(
    Selection.Validate(c, new[] { "69c3d43030f896ebef0ed357", "69ce62886e199f4bbe0ab19b" }, new Rules(), unavailable) != null,
    "Captured trader perk exclusion is enforced"
);
Check(vacuum.TraderMultiplier(therapist, "sell") == 1m, "Purchase effects do not change sale proceeds");
Check(vacuum.TraderMultiplier("custom-trader", "buy") == 1m, "Captured trader allow-list excludes custom traders");
Check(TraderPricing.Scale(10000, 1.2m).Equals(12000), "No extra currency unit from a float multiplier");
Check(TraderPricing.Scale(10000, 1.14m).Equals(11400), "Stacked multiplier stays exact at integral prices");
Check(TraderPricing.Required(TraderPricing.Scale(101, 0.95m), 1).Equals(96), "Fractional unit price rounds upward at purchase");
Check(TraderPricing.Required(TraderPricing.Scale(101, 0.95m), 2).Equals(192), "Bulk payment uses native quantity rounding");
Check(TraderPricing.Required(TraderPricing.Scale(1, 1.2m), 5).Equals(6), "Barter requirement rounds after quantity multiplication");
var neutral = new RuntimeEffects(c, Array.Empty<string>());
Check(bush.BushNoiseMultiplier.Equals(0.25f) && bush.BushSlowdownMultiplier.Equals(0.25f), "Captured bush multipliers");
Check(
    Math.Abs(BushInteraction.SpeedLimit(0.2f, bush.BushSlowdownMultiplier) - 0.8f) < 0.0001f,
    "Bushborne reduces an 80 percent slowdown to 20 percent"
);
Check(Math.Abs(BushInteraction.SpeedLimit(0.2f, neutral.BushSlowdownMultiplier) - 0.2f) < 0.0001f, "Removing Bushborne restores slowdown");
Check(neutral.BushNoiseMultiplier.Equals(1f), "Removing Bushborne restores noise");
Check(BushInteraction.SpeedLimit(1f, 0.25f).Equals(1f), "Bushborne adds no speed above the original limit");
var asymmetric = JsonConvert.DeserializeObject<Catalogue>(JsonConvert.SerializeObject(c))!;
var asymmetricEffect = asymmetric.All.Single(p => p.Id == "69c405a9d7a7b2ca660e0c56").Effects[0];
asymmetricEffect.PrimaryMultiplier = 0.4f;
asymmetricEffect.SecondaryMultiplier = 0.7f;
var asymmetricBush = new RuntimeEffects(asymmetric, new[] { "69c405a9d7a7b2ca660e0c56" });
Check(
    asymmetricBush.BushSlowdownMultiplier.Equals(0.4f) && asymmetricBush.BushNoiseMultiplier.Equals(0.7f),
    "Primary slowdown and secondary noise stay distinct"
);
var hideout = new RuntimeEffects(c, new[] { "69ce5eb3e4b79de94a0d78c8" });
Check(!hideout.HideoutRequiresFir(true), "No FiR relaxes hideout requirement");
Check(!hideout.HideoutRequiresFir(false), "No FiR preserves already unrestricted requirements");
Check(neutral.HideoutRequiresFir(true) && !neutral.HideoutRequiresFir(false), "Removal restores both original requirement states");
Check(
    CharacterSession.IsLoaded(selectedSeasonal, "seasonal", "seasonal-profile"),
    "Hideout effect enabled only after campaign identity finishes loading"
);
WTT.Campaigns.Tests.ConsumableChecks.Run(c, Check);
WTT.Campaigns.Tests.AllergyContainerChecks.Run(c, Check);
WTT.Campaigns.Tests.SerializationChecks.Run(c, Check);
WTT.Campaigns.Tests.HubChecks.Run(Check);
if (args.Length > 0)
{
    WTT.Campaigns.Tests.CompatibilityChecks.Run(args[0], Check);
}
Console.WriteLine($"PASS {count} assertions");
