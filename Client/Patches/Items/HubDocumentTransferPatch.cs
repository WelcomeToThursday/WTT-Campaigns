using System.Reflection;
using EFT.InventoryLogic;
using HarmonyLib;
using SeasonalPerks.Client.Hub;
using SPT.Reflection.Patching;

namespace SeasonalPerks.Client.Patches.Items;

internal class HubDocumentTransferPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(TransferResult), nameof(TransferResult.RaiseEvents));
    }

    [PatchPrefix]
    private static void Prefix(TransferResult __instance, CommandStatus status, IItemOwner controller)
    {
        HubDocuments.Transfer(__instance, __instance.Item, __instance.TargetItem, __instance.Count, false, status, controller);
    }
}
