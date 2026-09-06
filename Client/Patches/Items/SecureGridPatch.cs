using System.Reflection;
using Diz.LanguageExtensions;
using EFT;
using EFT.InventoryLogic;
using EFT.InventoryLogic.Operations;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace SeasonalPerks.Client.Patches.Items;

internal class SecureGridPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(Grid), nameof(Grid.CheckCompatibility));

    [PatchPostfix]
    private static void Postfix(Grid __instance, Item item, ref bool __result)
    {
        if (__result && SecureContainers.Reject(item, __instance.ParentItem))
            __result = false;
    }
}
