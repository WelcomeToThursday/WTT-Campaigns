using System.Reflection;
using HarmonyLib;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Routers;

namespace WTT.Campaigns.Server.Story;

[Injectable]
public sealed class StoryOutputPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(EventOutputHolder), nameof(EventOutputHolder.GetOutput));
    }

    [PatchPrefix]
    private static bool Prefix(MongoId sessionId, ref ItemEventRouterResponse __result)
    {
        var scope = StoryNativeScope.Current;
        if (scope == null || scope.Id != sessionId)
        {
            return true;
        }
        __result = scope.Output;
        return false;
    }
}
