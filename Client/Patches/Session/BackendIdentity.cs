using System.Reflection;
using EFT;
using HarmonyLib;
using Newtonsoft.Json;
using SeasonalPerks.Client.Profiles;
using SPT.Common.Http;
using SPT.Reflection.Patching;

namespace SeasonalPerks.Client.Patches.Session;

internal class BackendIdentity : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(TarkovApplication), nameof(TarkovApplication.CreateBackend));
    }

    [PatchPrefix]
    private static void Prefix(TarkovApplication __instance)
    {
        if (Plugin.PendingSessionId != null)
        {
            Plugin.SessionId = Plugin.PendingSessionId;
            Plugin.LogInfo("Seasonal switch/save: opening the requested backend session.");
        }
        if (Plugin.SessionId == null)
        {
            var snapshot = JsonConvert.DeserializeObject<ClientSnapshot>(
                RequestHandler.PostJson("/seasonal-perks/snapshot", "{}"),
                EftJsonConverters.Converters
            )!;
            Plugin.Accept(snapshot);
            Plugin.SessionId = snapshot.EffectiveProfileId;
        }
        AccessTools.Field(typeof(TarkovApplication), "_cachedPhpSessionId").SetValue(__instance, Plugin.SessionId);
    }
}
