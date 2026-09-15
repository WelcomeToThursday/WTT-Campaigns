using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Servers;

namespace WTT.Campaigns.Server.Editor;

/// <summary>Keep late native load/remove calls from touching profile files.</summary>
[Injectable]
public sealed class CampaignTestLoadProfilePatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(SaveServer), nameof(SaveServer.LoadProfileAsync));

    [PatchPrefix, UsedImplicitly]
    private static bool Prefix(MongoId sessionID, ref Task __result)
    {
        if (!CampaignTestSessions.IsTest(sessionID.ToString()))
            return true;
        __result = Task.CompletedTask;
        return false;
    }
}

[Injectable]
public sealed class CampaignTestRemoveProfilePatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(SaveServer), nameof(SaveServer.RemoveProfile));

    [PatchPrefix, UsedImplicitly]
    private static bool Prefix(MongoId sessionID, ref bool __result)
    {
        if (!CampaignTestSessions.IsTest(sessionID.ToString()))
            return true;
        __result = true;
        return false;
    }
}
