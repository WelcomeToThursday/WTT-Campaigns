using System.Reflection;
using Diz.LanguageExtensions;
using EFT.InventoryLogic;
using HarmonyLib;
using SeasonalPerks.Shared.Effects.Items;
using SPT.Reflection.Patching;

namespace SeasonalPerks.Client.Patches.Items;

internal class SecureAddPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(
            typeof(ItemManipulator),
            nameof(ItemManipulator.Add),
            new[] { typeof(Item), typeof(ItemAddress), typeof(ItemController), typeof(bool), typeof(bool) }
        );
    }

    [PatchPrefix]
    private static bool Prefix(Item item, ItemAddress to, bool ignoreRestrictions, ref OperationResult<AddResult> __result)
    {
        if (ignoreRestrictions || to.Container.ParentItem == null || !SecureContainers.Reject(item, to.Container.ParentItem))
        {
            return true;
        }

        __result = new StringError(SecureContainerRules.Message);
        return false;
    }
}
