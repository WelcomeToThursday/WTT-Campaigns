using System.Reflection;
using HarmonyLib;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Ws;

namespace WTT.Campaigns.Server.Story;

[Injectable]
public sealed class StoryNotificationPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(NotificationSendHelper), nameof(NotificationSendHelper.SendMessageAsync));
    }

    [PatchPrefix]
    private static bool Prefix(
        NotificationSendHelper __instance,
        MongoId sessionId,
        WsNotificationEvent notificationMessage,
        ref Task __result
    )
    {
        var scope = StoryNativeScope.Current;
        if (scope == null || scope.Id != sessionId)
        {
            return true;
        }
        scope.Notifications.Add(() => __instance.SendMessageAsync(sessionId, notificationMessage));
        __result = Task.CompletedTask;
        return false;
    }
}
