using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Models.Common;

namespace WTT.Campaigns.Server.Missions;

/// <summary>Fails an abandoned prepared/active mission when SPT opens a fresh local session.</summary>
[Injectable]
public sealed class MissionSessionRecoveryPatch(MissionService missions) : AbstractPatch
{
    private static MissionService _missions = null!;

    protected override MethodBase GetTargetMethod()
    {
        _missions = missions;
        return AccessTools.Method(typeof(GameController), nameof(GameController.GameStart));
    }

    [PatchPostfix]
    [UsedImplicitly]
    private static void Postfix(MongoId sessionId)
    {
        if (Editor.EditorSessions.IsScratch(sessionId.ToString()))
            return;
        _missions.AbandonSession(sessionId.ToString()).GetAwaiter().GetResult();
    }
}
