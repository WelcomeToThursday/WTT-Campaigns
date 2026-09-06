using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Models.Common;

namespace SeasonalPerks.Server.Patches.Session;

[Injectable]
public class NewClientSessionPatch(SeasonService seasons) : AbstractPatch
{
    private static SeasonService _seasons = null!;

    protected override MethodBase GetTargetMethod()
    {
        _seasons = seasons;
        return AccessTools.Method(typeof(GameController), nameof(GameController.GameStart));
    }

    [PatchPostfix]
    [UsedImplicitly]
    private static void Postfix(MongoId sessionId)
    {
        // Solo SPT starts a new client session after a crash; the previous local raid cannot resume.
        _seasons.MarkRaid(sessionId.ToString(), false).GetAwaiter().GetResult();
    }
}
