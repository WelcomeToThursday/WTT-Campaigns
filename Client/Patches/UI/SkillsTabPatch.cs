using System.Reflection;
using EFT.UI;
using HarmonyLib;
using SPT.Reflection.Patching;
using WTT.Campaigns.Client.UI;

namespace WTT.Campaigns.Client.Patches.UI;

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
            var component = __instance.GetComponent<CampaignSkillsTab>() ?? __instance.gameObject.AddComponent<CampaignSkillsTab>();
            component.Initialize(__instance, profile.Side == EFT.EPlayerSide.Savage);
        }
        catch (Exception exception)
        {
            Plugin.Error(exception);
        }
    }
}
