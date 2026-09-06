using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Models.Common;

namespace SeasonalPerks.Server.Patches.Session;

[Injectable]
public class RaidEndPatch(SeasonService seasons) : AbstractPatch
{
    private static SeasonService _seasons = null!;

    protected override MethodBase GetTargetMethod()
    {
        _seasons = seasons;
        return AccessTools.Method(typeof(MatchController), nameof(MatchController.EndLocalRaidAsync));
    }

    [PatchPostfix]
    [UsedImplicitly]
    private static void Postfix(MongoId sessionId, ref Task __result)
    {
        __result = Complete(__result, sessionId.ToString());
    }

    private static async Task Complete(Task original, string id)
    {
        await original;
        await _seasons.MarkRaid(id, false);
    }
}
