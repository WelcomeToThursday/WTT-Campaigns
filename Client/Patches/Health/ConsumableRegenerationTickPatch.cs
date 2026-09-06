using System.Reflection;
using EFT.HealthSystem;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace SeasonalPerks.Client.Patches.Health;

internal class ConsumableRegenerationTickPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod() =>
        AccessTools.Method(typeof(ActiveHealthController.HealthBoost), nameof(ActiveHealthController.HealthBoost.RegularUpdate));

    [PatchPrefix]
    private static bool Prefix(ActiveHealthController.HealthBoost __instance, float deltaTime)
    {
        if (!ConsumableHealthEffects.Regeneration.TryGetValue(__instance, out var effect))
            return true;
        ConsumableHealthEffects.Tick(__instance, effect, deltaTime);
        return false;
    }
}
