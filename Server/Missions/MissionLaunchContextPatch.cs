using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Routers;

namespace WTT.Campaigns.Server.Missions;

/// <summary>
/// Captures the mission marker at the native HTTP boundary. The raid start
/// patch consumes <see cref="MissionLaunchContext.Current"/> before SPT creates
/// a raid, which prevents a client-supplied body flag or a stale prepared run
/// from authorizing mission content.
/// </summary>
[Injectable]
public sealed class MissionLaunchContextPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        var target =
            AccessTools.Method(typeof(HttpRouter), "HandleRouteAsync")
            ?? throw new MissingMethodException(typeof(HttpRouter).FullName, "HandleRouteAsync");
        var parameters = target.GetParameters();
        if (
            target.ReturnType != typeof(ValueTask<bool>)
            || parameters.Length != 7
            || parameters[0].ParameterType != typeof(HttpRequest)
            || parameters[1].ParameterType != typeof(MongoId)
        )
            throw new InvalidOperationException("Unsupported SPT HTTP route dispatcher signature.");
        return target;
    }

    [PatchPrefix]
    [UsedImplicitly]
    private static void Prefix(HttpRequest request, MongoId sessionID, out MissionLaunchContext.Scope? __state)
    {
        __state = null;
        if (!string.Equals(request.Path.Value, "/client/match/local/start", StringComparison.OrdinalIgnoreCase))
            return;

        var values = request.Headers[MissionLaunchContext.HeaderName];
        // A duplicate header is retained as an invalid value. RaidStartPatch
        // then fails closed instead of accepting a comma-joined client value.
        var runId = values.Count == 1 ? values[0]?.Trim() ?? "" : "";
        __state = MissionLaunchContext.Push(sessionID.ToString(), values.Count > 0, runId);
    }

    [PatchPostfix]
    [UsedImplicitly]
    private static void Postfix(ref ValueTask<bool> __result, MissionLaunchContext.Scope? __state)
    {
        if (__state == null)
            return;

        // Start the wrapper while the marker is in the current execution
        // context, then restore the dispatcher's caller immediately. The
        // original ValueTask and its continuation retain the scoped marker;
        // Complete restores it in that continuation as well.
        __result = Complete(__result, __state);
        __state.Restore();
    }

    [PatchFinalizer]
    [UsedImplicitly]
    private static void Finalizer(Exception? __exception, MissionLaunchContext.Scope? __state)
    {
        if (__exception != null)
            __state?.Restore();
    }

    private static async ValueTask<bool> Complete(ValueTask<bool> original, MissionLaunchContext.Scope scope)
    {
        try
        {
            return await original.ConfigureAwait(false);
        }
        finally
        {
            scope.Restore();
        }
    }
}
