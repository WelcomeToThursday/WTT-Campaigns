using System.Reflection;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace SeasonalPerks.Client.Patches.Movement;

internal class SprintSpeedPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.PropertyGetter(typeof(MovementContext), nameof(MovementContext.StateSprintSpeedLimit));
    }

    [PatchPostfix]
    private static void Postfix(MovementContext __instance, ref float __result)
    {
        if (Plugin.SeasonalPlayer && ReferenceEquals(__instance, Plugin.Player!.MovementContext))
        {
            // This factor enters the target speed before acceleration/clamping; scaling
            // SprintSpeed's getter itself would feed its modified value back every frame.
            __result *= Plugin.Effects.Multiplier("sprint_speed_multiplicator");
        }
    }
}
