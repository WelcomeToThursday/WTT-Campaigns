using System.Reflection;
using HarmonyLib;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Match;

namespace SeasonalPerks.Server.Patches.Session;

[Injectable]
public class RaidStartPatch(SeasonService seasons) : AbstractPatch
{
    private static SeasonService _seasons = null!;

    protected override MethodBase GetTargetMethod()
    {
        _seasons = seasons;
        return AccessTools.Method(
            typeof(MatchController),
            nameof(MatchController.StartLocalRaidAsync)
        );
    }

    [PatchPrefix]
    private static void Prefix(MongoId sessionId)
    {
        _seasons.MarkRaid(sessionId.ToString(), true).GetAwaiter().GetResult();
    }

    [PatchPostfix]
    private static void Postfix(MongoId sessionId, ref Task<StartLocalRaidResponseData> __result)
    {
        __result = Complete(__result, sessionId.ToString());
    }

    private static async Task<StartLocalRaidResponseData> Complete(
        Task<StartLocalRaidResponseData> original,
        string id
    )
    {
        try
        {
            return await original;
        }
        catch
        {
            await _seasons.MarkRaid(id, false);
            throw;
        }
    }
}
