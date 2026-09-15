using System.Reflection;
using HarmonyLib;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Servers;

namespace WTT.Campaigns.Server.Editor;

[Injectable]
public sealed class EditorScratchSavePatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod() => AccessTools.Method(typeof(SaveServer), nameof(SaveServer.SaveProfileAsync));

    [PatchPrefix]
    private static bool Prefix(MongoId sessionID, ref Task<long> __result)
    {
        if (!EditorSessions.IsScratch(sessionID.ToString()))
            return true;
        __result = Task.FromResult(0L);
        return false;
    }
}
