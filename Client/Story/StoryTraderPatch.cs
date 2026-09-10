using System.Reflection;
using EFT.UI;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace WTT.Campaigns.Client.Story;

internal sealed class StoryTraderPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(
            typeof(TraderScreensGroup),
            nameof(TraderScreensGroup.Show),
            new[] { typeof(TraderScreensGroup.TraderScreenController) }
        );
    }

    [PatchPostfix]
    private static void Postfix(TraderScreensGroup __instance)
    {
        var host = __instance.GetComponent<StoryTraderHost>();
        if (StoryClient.Available)
        {
            (host ?? __instance.gameObject.AddComponent<StoryTraderHost>()).Attach(__instance);
        }
        else if (host)
        {
            host.Attach(__instance);
        }
    }
}
