using System.Reflection;
using HarmonyLib;
using SPT.Reflection.Patching;
using WTT.Campaigns.Client.Missions;

namespace WTT.Campaigns.Client.Patches.Session;

// Native LocalRaidStarted bypasses SPT.Common.Http.Client entirely.
internal sealed class NativeMissionLaunchPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod() =>
        AccessTools.Method(
            typeof(HTTPTransportManager),
            nameof(HTTPTransportManager.CreateHeadersForRequest),
            new[] { typeof(BackendRequestParams) }
        );

    [PatchPostfix]
    private static void Postfix(BackendRequestParams bRequest, Dictionary<string, string> __result)
    {
        RequestIdentity.ApplyNativeMissionMarker(
            bRequest.BackendMethod,
            MissionClient.CharacterId,
            __result,
            MissionRaidRuntime.PendingRunId
        );
    }
}
