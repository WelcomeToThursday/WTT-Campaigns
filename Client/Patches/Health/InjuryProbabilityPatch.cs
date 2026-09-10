using System.Reflection;
using EFT.HealthSystem;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace WTT.Campaigns.Client.Patches.Health;

internal class InjuryProbabilityPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(EffectsSettings.ProbabilitySetting), nameof(EffectsSettings.ProbabilitySetting.Try));
    }

    [PatchPrefix]
    private static void Prefix(EffectsSettings.ProbabilitySetting __instance, ref float cap)
    {
        if (!DamageContextPatch.IsSeasonalDamage)
        {
            return;
        }

        var settings = ActiveHealthController.Effect.EffectsSettings;
        var multiplier =
            ReferenceEquals(__instance, settings.LightBleeding.Probability)
            || ReferenceEquals(__instance, settings.HeavyBleeding.Probability)
                ? Plugin.Effects.Multiplier("bleeding_chance_multiplicator")
            : ReferenceEquals(__instance, settings.Fracture.BulletHitProbability)
            || ReferenceEquals(__instance, settings.Fracture.FallingProbability)
                ? Plugin.Effects.Multiplier("fracture_chance_multiplicator")
            : 1;

        // SPT tests Random(0, cap) against the existing probability curve. Dividing cap
        // scales the final probability while retaining thresholds, ammo bias and skill bonuses.
        cap /= Math.Max(0.001f, multiplier);
    }
}
