using System.Reflection;
using EFT.HealthSystem;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace WTT.Campaigns.Client.Patches.Health;

internal class FreshWoundPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.PropertyGetter(typeof(ActiveHealthController.Wound), nameof(ActiveHealthController.Wound.DefaultWorkTime));
    }

    [PatchPostfix]
    private static void Postfix(ActiveHealthController.Wound __instance, ref float __result)
    {
        if (
            Plugin.InRaid
            && Plugin.SeasonalPlayer
            && ReferenceEquals(__instance.HealthController, Plugin.Player!.ActiveHealthController)
            && Plugin.Effects.Has("fresh_wounds_until_raid_end")
        )
        {
            // Live Wound.DefaultWorkTime, RVA 0x40B20A0, uses positive infinity.
            // Preserve the existing bleed build-up and normal raid-end health serialization.
            __result = float.PositiveInfinity;
        }
    }
}
