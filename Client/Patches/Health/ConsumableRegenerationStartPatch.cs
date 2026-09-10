using System.Reflection;
using EFT.HealthSystem;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace WTT.Campaigns.Client.Patches.Health;

internal class ConsumableRegenerationStartPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(ActiveHealthController.HealthBoost), nameof(ActiveHealthController.HealthBoost.Started));
    }

    [PatchPrefix]
    private static bool Prefix(ActiveHealthController.HealthBoost __instance)
    {
        if (!ConsumableHealthEffects.Regeneration.TryGetValue(__instance, out var effect))
        {
            return true;
        }

        __instance.SetHealthRatesPerSecond(effect.Health, effect.Energy, effect.Hydration, 0);
        return false;
    }
}
