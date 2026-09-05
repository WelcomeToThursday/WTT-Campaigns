using System.Net.Http;
using System.Reflection;
using BepInEx;
using EFT;
using HarmonyLib;
using Newtonsoft.Json;
using SeasonalPerks.Shared;
using SPT.Common.Http;
using SPT.Reflection.Patching;
using UnityEngine;

namespace SeasonalPerks.Client.Patches.Session;

internal class BackendIdentity : ModulePatch
{
    protected override MethodBase GetTargetMethod() =>
        AccessTools.Method(typeof(TarkovApplication), nameof(TarkovApplication.CreateBackend));

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
            var snapshot = JsonConvert.DeserializeObject<Snapshot>(
                RequestHandler.PostJson("/seasonal-perks/snapshot", "{}")
            )!;
            Plugin.Accept(snapshot);
            Plugin.SessionId = snapshot.EffectiveProfileId;
        }
        AccessTools
            .Field(typeof(TarkovApplication), "_cachedPhpSessionId")
            .SetValue(__instance, Plugin.SessionId);
    }
}
