using System.Reflection;
using EFT.HealthSystem;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace SeasonalPerks.Client.Patches.Items;

internal class MedicineCompletionPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod() =>
        AccessTools.Method(
            typeof(ActiveHealthController.MedEffect),
            nameof(ActiveHealthController.MedEffect.Residue)
        );

    [PatchPrefix]
    private static void Prefix(ActiveHealthController.MedEffect __instance) =>
        ConsumableUsePatch.CompleteMedicine(__instance);
}
