using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Match;
using WTT.Campaigns.Server.Hub;
using WTT.Campaigns.Server.Profiles;

namespace WTT.Campaigns.Server.Patches.Session;

[Injectable]
public class RaidEndPatch(SeasonService seasons, HubGameplay hub, WTT.Campaigns.Server.Story.StoryService story) : AbstractPatch
{
    private static SeasonService _seasons = null!;
    private static HubGameplay _hub = null!;
    private static WTT.Campaigns.Server.Story.StoryService _story = null!;

    protected override MethodBase GetTargetMethod()
    {
        _seasons = seasons;
        _hub = hub;
        _story = story;
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
        if (_hub.RaidFinished(sessionId.ToString(), request.ServerId))
        {
            __result = Task.CompletedTask;
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

    private static async Task Complete(Task original, string id, EndLocalRaidRequestData request, IDisposable lease)
    {
        using (lease)
        {
            await original;
            await _hub.FinishRaid(id, request, true);
            await _story.FinishRaid(id, request);
        }
        await _seasons.MarkRaid(id, false);
    }
}
