using System.Reflection;
using Diz.LanguageExtensions;
using EFT.InventoryLogic;
using HarmonyLib;
using SeasonalPerks.Shared.Effects.Items;
using SPT.Reflection.Patching;

namespace SeasonalPerks.Client.Patches.Items;

internal class SecureMovePatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(ItemManipulator), nameof(ItemManipulator.MovePathCheck));
    }

    [PatchPrefix]
    private static bool Prefix(Item item, ItemAddress to, ref Option<None> __result)
    {
        if (to.Container.ParentItem == null || !SecureContainers.Reject(item, to.Container.ParentItem))
        {
            return true;
        }

        __result = new StringError(SecureContainerRules.Message);
        return false;
    }
}
