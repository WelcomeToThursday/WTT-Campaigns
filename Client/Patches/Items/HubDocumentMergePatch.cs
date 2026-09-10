using System.Reflection;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using WTT.Campaigns.Client.Hub;

namespace WTT.Campaigns.Client.Patches.Items;

internal class HubDocumentMergePatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(MergeResult), nameof(MergeResult.RaiseEvents));
    }

    [PatchPrefix]
    private static void Prefix(MergeResult __instance, CommandStatus status, IItemOwner controller)
    {
        HubDocuments.Transfer(
            __instance,
            __instance.Item,
            __instance.TargetItem,
            __instance._transferResult.Count,
            false,
            status,
            controller
        );
    }
}
