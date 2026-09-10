using System.Reflection;
using EFT.HealthSystem;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace WTT.Campaigns.Client.Patches.Health;

internal class EnergyDrainPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(ActiveHealthController.Existence), nameof(ActiveHealthController.Existence.GetEnergyDamage));
    }

    [PatchPostfix]
    private static void Postfix(ActiveHealthController.Existence __instance, ref float __result)
    {
        if (Plugin.SeasonalPlayer && ReferenceEquals(__instance.HealthController, Plugin.Player!.ActiveHealthController))
        {
            __result *= Plugin.Effects.Multiplier("energy_drain_multiplicator");
        }
    }
}
