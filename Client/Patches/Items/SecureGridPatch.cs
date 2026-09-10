using System.Reflection;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace WTT.Campaigns.Client.Patches.Items;

internal class SecureGridPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(Grid), nameof(Grid.CheckCompatibility));
    }

    [PatchPostfix]
    private static void Postfix(Grid __instance, Item item, ref bool __result)
    {
        if (__result && SecureContainers.Reject(item, __instance.ParentItem))
        {
            __result = false;
        }
    }
}
