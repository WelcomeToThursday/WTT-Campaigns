using System.Reflection;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace SeasonalPerks.Client.Patches.Movement;

// Disabled/destroyed triggers may never send OnTriggerExit. Restore the native limit
// on the next movement tick, also covering a collider disabled during a raid.
internal class BushCleanupPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod() =>
        AccessTools.Method(typeof(MovementContext), nameof(MovementContext.ProcessSpeedLimits));

    [PatchPostfix]
    private static void Postfix(MovementContext __instance)
    {
        if (
            Plugin.SeasonalPlayer
            && ReferenceEquals(__instance, Plugin.Player!.MovementContext)
            && BushOccupancy.RemoveInactive(__instance)
        )
            __instance.RefreshObstacleRestrictions();
    }
}
