using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Match;
using WTT.Campaigns.Server.Hub;
using WTT.Campaigns.Server.Missions;
using WTT.Campaigns.Server.Profiles;

namespace WTT.Campaigns.Server.Patches.Session;

[Injectable]
public class RaidEndPatch(SeasonService seasons, HubGameplay hub, WTT.Campaigns.Server.Story.StoryService story, MissionService missions)
    : AbstractPatch
{
    private static SeasonService _seasons = null!;
    private static HubGameplay _hub = null!;
    private static WTT.Campaigns.Server.Story.StoryService _story = null!;
    private static MissionService _missions = null!;

    protected override MethodBase GetTargetMethod()
    {
        _seasons = seasons;
        _hub = hub;
        _story = story;
        _missions = missions;
        return AccessTools.Method(typeof(MatchController), nameof(MatchController.EndLocalRaidAsync));
    }

    [PatchPostfix]
    [UsedImplicitly]
    private static void Postfix(MongoId sessionId, EndLocalRaidRequestData request, IDisposable __state, ref Task __result)
    {
        if (__state != null)
            __result = Complete(__result, sessionId.ToString(), request, __state);
    }

    [PatchPrefix]
    [UsedImplicitly]
    private static bool Prefix(MongoId sessionId, EndLocalRaidRequestData request, out IDisposable __state, ref Task __result)
    {
        if (Editor.EditorSessions.IsScratch(sessionId.ToString()))
        {
            __state = null!;
            __result = Task.CompletedTask;
            return false;
        }
        __state = _seasons.Enter(_seasons.ResolveRoot(sessionId.ToString()));
        var hubFinished = _hub.RaidFinished(sessionId.ToString(), request.ServerId);
        var missionFinished = _missions.RaidFinished(sessionId.ToString(), request.ServerId);
        if (!RaidFinalization.RequiresNative(hubFinished, missionFinished))
        {
            // Native raid reconciliation already ran for one of the campaign
            // subsystems. Finish the remaining subsystem against the request
            // without invoking the native end method a second time.
            var lease = __state;
            __state = null!;
            __result = Complete(Task.CompletedTask, sessionId.ToString(), request, lease);
            return false;
        }
        return true;
    }

    [PatchFinalizer]
    [UsedImplicitly]
    private static void Finalizer(Exception? __exception, IDisposable? __state)
    {
        if (__exception != null)
        {
            __state?.Dispose();
        }
    }

    private static Task Complete(Task original, string id, EndLocalRaidRequestData request, IDisposable lease) =>
        RaidFinalization.Complete(
            original,
            lease,
            () => _hub.FinishRaid(id, request, true),
            () => _story.FinishRaid(id, request),
            () => _missions.FinishRaid(id, request, true),
            () => _seasons.MarkRaid(id, false)
        );
}
