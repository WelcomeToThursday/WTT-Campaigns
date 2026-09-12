using System.Net.WebSockets;
using System.Reflection;
using EFT.Communications;
using HarmonyLib;
using SPT.Reflection.Patching;
using WTT.Campaigns.Client.Authoring;

namespace WTT.Campaigns.Client.Patches.Session;

internal sealed class AuthoringNotificationSocket : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(LongPollingWebSocketRequest), nameof(LongPollingWebSocketRequest.Receive));
    }

    [PatchPrefix]
    private static void Prefix(LongPollingWebSocketRequest __instance, WebSocket webSocket, out bool __state)
    {
        __state = __instance.GetUri().AbsolutePath.StartsWith("/notifierServer/getwebsocket/", StringComparison.Ordinal);
        if (__state)
        {
            AuthoringSocket.Shared.Attach(__instance, webSocket);
        }
    }

    [PatchPostfix]
    private static void Postfix(LongPollingWebSocketRequest __instance, WebSocket webSocket, bool __state, ref Task __result)
    {
        if (__state)
        {
            __result = Track(__result, __instance, webSocket);
        }
    }

    private static async Task Track(Task receive, object owner, WebSocket socket)
    {
        try
        {
            await receive.ConfigureAwait(false);
        }
        finally
        {
            AuthoringSocket.Shared.Detach(owner, socket);
        }
    }
}

internal sealed class AuthoringNotificationReply : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.PropertyGetter(typeof(LongPollingRequestAbstract), nameof(LongPollingRequestAbstract.OnReceiveMessage));
    }

    [PatchPostfix]
    private static void Postfix(LongPollingRequestAbstract __instance, ref Action<long, byte[]> __result)
    {
        if (__instance is not LongPollingWebSocketRequest)
        {
            return;
        }

        var native = __result;
        __result = (code, data) =>
        {
            if (!AuthoringSocket.Shared.Receive(__instance, data))
            {
                native(code, data);
            }
        };
    }
}
