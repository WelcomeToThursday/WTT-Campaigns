using System.Reflection;
using HarmonyLib;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Servers;

namespace WTT.Campaigns.Server.Story;

[Injectable]
public sealed class StorySavePatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(SaveServer), nameof(SaveServer.SaveProfileAsync));
    }

    [PatchPrefix]
    private static bool Prefix(MongoId sessionID, ref Task<long> __result)
    {
        var scope = StoryNativeScope.Current;
        if (scope == null || scope.Id != sessionID)
        {
            return true;
        }
        __result = Task.FromResult(0L);
        return false;
    }
}
