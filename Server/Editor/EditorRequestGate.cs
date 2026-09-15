using System.Reflection;
using HarmonyLib;
using Microsoft.AspNetCore.Http;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Routers;
using WTT.Campaigns.Shared.Authoring;

namespace WTT.Campaigns.Server.Editor;

[Injectable]
public sealed class EditorRequestGate(EditorSessions editor) : AbstractPatch
{
    private static EditorSessions _editor = null!;
    private static PropertyInfo _output = null!;

    protected override MethodBase GetTargetMethod()
    {
        _editor = editor;
        // SPT 4.1.5 changed the outer response method to return streamed objects.
        // The route dispatcher and its string Output slot are shared by 4.1.x.
        var target =
            AccessTools.Method(typeof(HttpRouter), "HandleRouteAsync")
            ?? throw new MissingMethodException(typeof(HttpRouter).FullName, "HandleRouteAsync");
        var parameters = target.GetParameters();
        if (
            target.ReturnType != typeof(ValueTask<bool>)
            || parameters.Length != 7
            || parameters[0].Name != "request"
            || parameters[0].ParameterType != typeof(HttpRequest)
            || parameters[1].Name != "sessionID"
            || parameters[1].ParameterType != typeof(MongoId)
            || parameters[2].Name != "wrapper"
        )
            throw new InvalidOperationException("Unsupported SPT HTTP route dispatcher signature.");
        _output =
            AccessTools.Property(parameters[2].ParameterType, "Output")
            ?? throw new MissingMemberException(parameters[2].ParameterType.FullName, "Output");
        if (_output.PropertyType != typeof(string) || !_output.CanWrite)
            throw new InvalidOperationException("Unsupported SPT HTTP response wrapper.");
        return target;
    }

    [PatchPrefix]
    private static bool Prefix(HttpRequest request, MongoId sessionID, object wrapper, ref ValueTask<bool> __result)
    {
        _editor.RecoverAbandoned(sessionID.ToString());
        if (!EditorSessions.Restricted(sessionID.ToString()) || !EditorPolicy.Blocks(request.Path.Value ?? ""))
            return true;
        _output.SetValue(
            wrapper,
            "{\"err\":228,\"errmsg\":\"This action is unavailable in Campaign Editor.\",\"data\":null,\"Error\":\"Return to game before using gameplay features.\"}"
        );
        // Mark the request handled so neither static nor dynamic gameplay routes run.
        __result = ValueTask.FromResult(true);
        return false;
    }
}
