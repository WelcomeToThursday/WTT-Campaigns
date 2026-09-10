using System.Reflection;
using Diz.LanguageExtensions;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using WTT.Campaigns.Shared.Effects.Items;

namespace WTT.Campaigns.Client.Patches.Items;

internal class SecureTransferPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(ItemManipulator), nameof(ItemManipulator.TransferMaxStackCount));
    }

    [PatchPrefix]
    private static bool Prefix(Item source, Item target, ref OperationResult<TransferResult> __result)
    {
        if (!SecureContainers.Reject(source, target))
        {
            return true;
        }

        __result = new StringError(SecureContainerRules.Message);
        return false;
    }
}
