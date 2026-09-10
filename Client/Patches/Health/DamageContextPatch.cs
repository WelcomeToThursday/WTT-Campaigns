using System.Reflection;
using EFT;
using EFT.Ballistics;
using EFT.HealthSystem;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace WTT.Campaigns.Client.Patches.Health;

internal class DamageContextPatch : ModulePatch
{
    [ThreadStatic]
    internal static bool IsSeasonalDamage;

    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(ActiveHealthController), nameof(ActiveHealthController.ApplyDamage));
    }

    [PatchPrefix]
    private static void Prefix(ActiveHealthController __instance, DamageInfo damageInfo, ref float damage, out bool __state)
    {
        __state = IsSeasonalDamage;
        IsSeasonalDamage = Plugin.SeasonalPlayer && ReferenceEquals(__instance, Plugin.Player!.ActiveHealthController);
        if (IsSeasonalDamage && damageInfo.DamageType == EDamageType.Fall)
        {
            damage *= Plugin.Effects.Multiplier("fall_damage_multiplicator");
        }
    }

    [PatchFinalizer]
    private static Exception? Finalizer(Exception? __exception, bool __state)
    {
        IsSeasonalDamage = __state;
        return __exception;
    }
}
