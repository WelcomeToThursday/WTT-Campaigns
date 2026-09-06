using System.Reflection;
using EFT;
using HarmonyLib;
using SeasonalPerks.Client.Hub;
using SPT.Reflection.Patching;

namespace SeasonalPerks.Client.Patches.Items;

internal class HubDocumentRaidPatch(string method) : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(EftClientBackendSession), method);
    }

    [PatchPrefix]
    private static void Prefix()
    {
        if (Plugin.Current?.ActiveMode == "seasonal")
        {
            // Do not submit extraction or abandon the previous raid while its document operations are uncertain.
            HubDocuments.FlushRequired();
        }
    }
}
