using System.Reflection;
using EFT;
using HarmonyLib;
using SeasonalPerks.Shared;
using SPT.Reflection.Patching;

namespace SeasonalPerks.Client.Patches.Movement;

internal class BushMovementPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod() =>
        AccessTools.Method(
            typeof(MovementContext),
            nameof(MovementContext.AddStateSpeedLimit),
            new[] { typeof(float), typeof(Player.ESpeedLimit) }
        );

    [PatchPrefix]
    private static void Prefix(
        MovementContext __instance,
        ref float speedLimit,
        Player.ESpeedLimit cause
    )
    {
        if (
            cause == Player.ESpeedLimit.Swamp
            && Plugin.SeasonalPlayer
            && ReferenceEquals(__instance, Plugin.Player!.MovementContext)
            && BushOccupancy.Contains(__instance)
        )
            speedLimit = BushInteraction.SpeedLimit(
                speedLimit,
                Plugin.Effects.BushSlowdownMultiplier
            );
    }
}
