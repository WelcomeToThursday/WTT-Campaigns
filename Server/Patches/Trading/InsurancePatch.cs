using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using SeasonalPerks.Server.Profiles;
using SeasonalPerks.Shared.Effects;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Routers;

namespace SeasonalPerks.Server.Patches.Trading;

[Injectable]
public class InsurancePatch(EventOutputHolder output) : AbstractPatch
{
    private static EventOutputHolder _output = null!;

    protected override MethodBase GetTargetMethod()
    {
        _output = output;
        return AccessTools.Method(typeof(InsuranceController), nameof(InsuranceController.Insure));
    }

    [PatchPrefix]
    [UsedImplicitly]
    private static bool Prefix(PmcData pmcData, MongoId sessionId, ref ItemEventRouterResponse __result)
    {
        var effects = ServerStartup.Seasons.Effects(pmcData);
        if (!effects.Has("insurance_disabled"))
        {
            return true;
        }

        // The item-event batch ultimately serializes its shared output holder.
        __result = _output.GetOutput(sessionId);
        __result.Warnings ??= [];
        __result.Warnings.Add(new Warning { Index = 0, ErrorMessage = "Insurance is disabled for this seasonal character." });
        return false;
    }
}
