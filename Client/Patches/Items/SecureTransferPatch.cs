using System.Reflection;
using Diz.LanguageExtensions;
using EFT;
using EFT.InventoryLogic;
using EFT.InventoryLogic.Operations;
using HarmonyLib;
using SeasonalPerks.Shared.Effects.Items;
using SPT.Reflection.Patching;

namespace SeasonalPerks.Client.Patches.Items;

internal class SecureTransferPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod() =>
        AccessTools.Method(typeof(ItemManipulator), nameof(ItemManipulator.TransferMaxStackCount));

    [PatchPrefix]
    private static bool Prefix(Item source, Item target, ref OperationResult<TransferResult> __result)
    {
        if (!SecureContainers.Reject(source, target))
            return true;
        __result = new StringError(SecureContainerRules.Message);
        return false;
    }
}
