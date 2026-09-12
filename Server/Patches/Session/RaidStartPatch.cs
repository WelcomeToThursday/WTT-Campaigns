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
public class RaidStartPatch(SeasonService seasons, HubGameplay hub, WTT.Campaigns.Server.Story.StoryService story) : AbstractPatch
{
    private static SeasonService _seasons = null!;
    private static HubGameplay _hub = null!;
    private static WTT.Campaigns.Server.Story.StoryService _story = null!;

    protected override MethodBase GetTargetMethod()
    {
        _seasons = seasons;
        _hub = hub;
        _story = story;
        return AccessTools.Method(typeof(MatchController), nameof(MatchController.StartLocalRaidAsync));
    }

    [PatchPrefix]
    [UsedImplicitly]
    private static void Prefix(MongoId sessionId)
    {
        _seasons.MarkRaid(sessionId.ToString(), true).GetAwaiter().GetResult();
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
            await _hub.StartRaid(id, request, result);
            await _story.StartRaid(id, request, result);
            await _seasons.MarkRaid(id, true, result.ServerId);
            return result;
        }
        catch
        {
            await _seasons.MarkRaid(id, false);
            throw;
        }
    }
}
