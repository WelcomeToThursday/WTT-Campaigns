using System.Reflection;
using EFT;
using HarmonyLib;
using Newtonsoft.Json;
using SPT.Common.Http;
using SPT.Reflection.Patching;
using WTT.Campaigns.Client.Profiles;
using WTT.Campaigns.Shared.Contracts;

namespace WTT.Campaigns.Client.Patches.Session;

internal class BackendIdentity : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(TarkovApplication), nameof(TarkovApplication.CreateBackend));
    }

    [PatchPrefix]
    private static void Prefix(TarkovApplication __instance)
    {
        WTT.Campaigns.Client.Progression.ProgressionClient.Reset();
        if (Plugin.PendingSessionId != null)
        {
            Plugin.SessionId = Plugin.PendingSessionId;
            Plugin.LogInfo("WTT-Campaigns switch/save: opening the requested backend session.");
        }
        if (Plugin.SessionId == null)
        {
            var snapshot = JsonConvert.DeserializeObject<ClientSnapshot>(
                RequestHandler.PostJson("/wtt-campaigns/snapshot", JsonConvert.SerializeObject(new Mutation { ProtocolVersion = 2 })),
                EftJsonConverters.Converters
            )!;
            Plugin.Accept(snapshot);
            Plugin.SessionId = snapshot.EffectiveProfileId;
        }
        AccessTools.Field(typeof(TarkovApplication), "_cachedPhpSessionId").SetValue(__instance, Plugin.SessionId);
    }
}
