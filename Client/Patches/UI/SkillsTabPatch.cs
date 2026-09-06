using System.Reflection;
using EFT.UI;
using HarmonyLib;
using SeasonalPerks.Client.UI;
using SPT.Reflection.Patching;

namespace SeasonalPerks.Client.Patches.UI;

internal sealed class SkillsTabPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(SkillsAndMasteringScreen), nameof(SkillsAndMasteringScreen.Show));
    }

    [PatchPostfix]
    private static void Postfix(SkillsAndMasteringScreen __instance, EFT.Profile profile)
    {
        // Show activates the screen, allowing Awake to create the native tab group.
        try
        {
            var component = __instance.GetComponent<SeasonalSkillsTab>() ?? __instance.gameObject.AddComponent<SeasonalSkillsTab>();
            component.Initialize(__instance, profile.Side == EFT.EPlayerSide.Savage);
        }
        catch (Exception exception)
        {
            Plugin.Error(exception);
        }
    }
}
