using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Servers;

namespace WTT.Campaigns.Server.Editor;

/// <summary>
/// Full campaign tests use SaveServer's live profile dictionary for native
/// gameplay, but their save callback must never reach user/profiles. Tombstones
/// remain recognized after teardown so late callbacks are no-ops too.
/// </summary>
[Injectable]
public sealed class CampaignTestSavePatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(SaveServer), nameof(SaveServer.SaveProfileAsync));

    [PatchPrefix, UsedImplicitly]
    private static bool Prefix(MongoId sessionID, ref Task<long> __result)
    {
        if (!CampaignTestSessions.IsTest(sessionID.ToString()))
            return true;
        __result = Task.FromResult(0L);
        return false;
    }
}
