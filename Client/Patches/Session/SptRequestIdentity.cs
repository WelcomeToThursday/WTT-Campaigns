using System.Reflection;
using HarmonyLib;
using SPT.Common.Http;
using SPT.Reflection.Patching;

namespace SeasonalPerks.Client.Patches.Session;

internal class SptRequestIdentity : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(SPT.Common.Http.Client), nameof(SPT.Common.Http.Client.CreateNewHttpRequest));
    }

    [PatchPostfix]
    private static void Postfix(SPT.Common.Http.Client __instance, string path, HttpRequestMessage __result)
    {
        // Each request captures its identity at creation, including requests already in flight.
        if (
            !ReferenceEquals(__instance, RequestHandler.HttpClient)
            || path.StartsWith("/wtt-seasonal/", StringComparison.Ordinal)
            || string.IsNullOrEmpty(Plugin.SessionId)
        )
        {
            return;
        }

        __result.Headers.Remove("Cookie");
        __result.Headers.Add("Cookie", "PHPSESSID=" + Plugin.SessionId);
    }
}
