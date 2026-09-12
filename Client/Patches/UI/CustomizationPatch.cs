using System.Reflection;
using EFT.UI;
using HarmonyLib;
using SPT.Reflection.Patching;
using WTT.Campaigns.Client.Customization;

namespace WTT.Campaigns.Client.Patches.UI;

internal sealed class CustomizationPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod() =>
        AccessTools.Method(
            typeof(InventoryScreen),
            nameof(InventoryScreen.Show),
            new[] { typeof(InventoryScreen.InventoryScreenController) }
        );

    [PatchPrefix]
    private static void Prefix(InventoryScreen __instance, InventoryScreen.InventoryScreenController controller)
    {
        try
        {
            (__instance.GetComponent<CustomizationTab>() ?? __instance.gameObject.AddComponent<CustomizationTab>()).Initialize(
                __instance,
                controller
            );
        }
        catch (Exception exception)
        {
            Plugin.Error(exception);
        }
    }
}

internal sealed class CustomizationClosePatch : ModulePatch
{
    protected override MethodBase GetTargetMethod() =>
        AccessTools.Method(
            typeof(InventoryScreen.InventoryScreenController),
            nameof(InventoryScreen.InventoryScreenController.CloseScreenInterruption)
        );

    [PatchPostfix]
    private static void Postfix(InventoryScreen.InventoryScreenController __instance, ref Task<bool> __result)
    {
        __result = Flush(__result, __instance.Screen?.GetComponent<CustomizationTab>());
    }

    private static async Task<bool> Flush(Task<bool> original, CustomizationTab? tab)
    {
        if (!await original)
            return false;
        return tab == null || await tab.Flush();
    }
}

internal sealed class CustomizationCleanupPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(InventoryScreen), nameof(InventoryScreen.Close));

    [PatchPrefix]
    private static void Prefix(InventoryScreen __instance) => __instance.GetComponent<CustomizationTab>()?.Close();
}

internal sealed class CustomizationPreviewPatch(string method) : ModulePatch
{
    protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(HeadSelectionState), method);

    [PatchPrefix]
    private static bool Prefix(HeadSelectionState __instance, MethodBase __originalMethod, object[] __args, ref Task __result)
    {
        if (!AppearanceView.TryGet(__instance, out var view))
            return true;
        __result =
            __originalMethod.Name == nameof(HeadSelectionState.UpdatePreview) ? view.UpdatePreview() : view.PlayVoice((int)__args[0]);
        return false;
    }
}
