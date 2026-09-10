using System.Reflection;
using HarmonyLib;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Servers;

namespace WTT.Campaigns.Server.Story;

[Injectable]
public sealed class StoryProfilePatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(SaveServer), nameof(SaveServer.GetProfile));
    }

    [PatchPrefix]
    private static bool Prefix(MongoId sessionId, ref SptProfile __result)
    {
        var scope = StoryNativeScope.Current;
        if (scope == null || scope.Id != sessionId)
        {
            return true;
        }
        __result = scope.Profile;
        return false;
    }
}
