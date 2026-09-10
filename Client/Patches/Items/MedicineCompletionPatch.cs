using System.Reflection;
using EFT.HealthSystem;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace WTT.Campaigns.Client.Patches.Items;

internal class MedicineCompletionPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(ActiveHealthController.MedEffect), nameof(ActiveHealthController.MedEffect.Residue));
    }

    [PatchPrefix]
    private static void Prefix(ActiveHealthController.MedEffect __instance)
    {
        ConsumableUsePatch.CompleteMedicine(__instance);
    }
}
