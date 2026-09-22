using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Servers;

namespace WTT.Campaigns.Server.Editor;

/// <summary>
/// Route active test saves to the durable test envelope. Unloaded and retired
/// test identities remain no-ops, so late callbacks cannot create native files.
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
        __result = CampaignTestSessions.SaveTest(sessionID.ToString());
        return false;
    }
}
