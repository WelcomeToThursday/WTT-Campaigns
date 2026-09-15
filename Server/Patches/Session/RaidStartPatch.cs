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
public class RaidStartPatch(SeasonService seasons, HubGameplay hub, WTT.Campaigns.Server.Story.StoryService story, MissionService missions)
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
        return AccessTools.Method(typeof(MatchController), nameof(MatchController.StartLocalRaidAsync));
    }

    [PatchPrefix]
    [UsedImplicitly]
    private static void Prefix(MongoId sessionId, StartLocalRaidRequestData request)
    {
        var id = sessionId.ToString();
        if (Editor.EditorSessions.IsScratch(sessionId.ToString()))
        {
            var editor = Editor.EditorSessions.Find(sessionId.ToString());
            if (
                editor?.Ready != true
                || editor.Location.Length == 0
                || editor.Location != request.Location
                || DateTimeOffset.UtcNow - editor.Contact > TimeSpan.FromMinutes(1)
            )
                throw new InvalidOperationException("Open the map from editor home.");
            return;
        }

        // The marker is captured from the authenticated HTTP request by
        // MissionLaunchContextPatch. A body or editor flag never authorizes a
        // mission launch. A missing marker means this is an ordinary native
        // raid, so retire a still-prepared mission before allowing that raid
        // to start.
        var marker = MissionLaunchContext.Current;
        if (marker?.HeaderPresent == true)
        {
            if (!string.Equals(marker.SessionId, id, StringComparison.Ordinal))
                throw new InvalidOperationException("The mission launch identity does not match this character.");
            if (marker.RunId.Length == 0)
                throw new InvalidOperationException("The mission launch marker is invalid.");
            _missions.ValidateNativeLaunchMarker(id, request.Location, marker.RunId);
        }
        else
        {
            _missions.CancelPreparedForOrdinaryStart(id).GetAwaiter().GetResult();
        }

        _seasons.MarkRaid(id, true).GetAwaiter().GetResult();
    }

    [PatchPostfix]
    [UsedImplicitly]
    private static void Postfix(MongoId sessionId, StartLocalRaidRequestData request, ref Task<StartLocalRaidResponseData> __result)
    {
        __result = Complete(__result, sessionId.ToString(), request);
    }

    private static async Task<StartLocalRaidResponseData> Complete(
        Task<StartLocalRaidResponseData> original,
        string id,
        StartLocalRaidRequestData request
    )
    {
        try
        {
            var result = await original;
            if (Editor.EditorSessions.IsScratch(id))
                return result;
            await _hub.StartRaid(id, request, result);
            await _story.StartRaid(id, request, result);
            await _missions.StartRaid(id, request, result);
            await _seasons.MarkRaid(id, true, result.ServerId);
            return result;
        }
        catch
        {
            if (!Editor.EditorSessions.IsScratch(id))
                await _seasons.MarkRaid(id, false);
            throw;
        }
    }
}
