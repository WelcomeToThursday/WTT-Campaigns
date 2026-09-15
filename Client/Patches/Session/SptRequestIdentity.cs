using System.Reflection;
using HarmonyLib;
using SPT.Common.Http;
using SPT.Reflection.Patching;

namespace WTT.Campaigns.Client.Patches.Session;

internal class SptRequestIdentity : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(SPT.Common.Http.Client), nameof(SPT.Common.Http.Client.CreateNewHttpRequest));
    }

    [PatchPostfix]
    private static void Postfix(SPT.Common.Http.Client __instance, string path, HttpRequestMessage __result)
    {
        RequestIdentity.Apply(
            ReferenceEquals(__instance, RequestHandler.HttpClient),
            path,
            Plugin.SessionId,
            __result,
            Authoring.CampaignTestMode.Active,
            Missions.MissionRaidRuntime.PendingRunId
        );
    }
}
