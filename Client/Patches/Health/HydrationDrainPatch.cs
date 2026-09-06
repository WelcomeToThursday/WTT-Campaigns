using System.Reflection;
using EFT.HealthSystem;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace SeasonalPerks.Client.Patches.Health;

internal class HydrationDrainPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(ActiveHealthController.Existence), nameof(ActiveHealthController.Existence.GetHydrationDamage));
    }

    [PatchPostfix]
    private static void Postfix(ActiveHealthController.Existence __instance, ref float __result)
    {
        if (Plugin.SeasonalPlayer && ReferenceEquals(__instance.HealthController, Plugin.Player!.ActiveHealthController))
        {
            __result *= Plugin.Effects.Multiplier("hydration_drain_multiplicator");
        }
    }
}
