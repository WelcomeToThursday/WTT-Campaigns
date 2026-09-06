using System.Reflection;
using EFT.InventoryLogic;
using HarmonyLib;
using SeasonalPerks.Client.Hub;
using SPT.Reflection.Patching;

namespace SeasonalPerks.Client.Patches.Items;

internal class HubDocumentSplitPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(SplitResult), nameof(SplitResult.RaiseEvents));
    }

    [PatchPrefix]
    private static void Prefix(SplitResult __instance, CommandStatus status, IItemOwner controller)
    {
        HubDocuments.Transfer(__instance, __instance.Item, __instance.ResultItem, __instance.Count, true, status, controller);
    }
}
