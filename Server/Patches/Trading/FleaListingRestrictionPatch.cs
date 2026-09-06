using System.Reflection;
using HarmonyLib;
using SeasonalPerks.Server.Effects;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;

namespace SeasonalPerks.Server.Patches.Trading;

[Injectable]
public class FleaListingRestrictionPatch(FleaRestrictions restrictions) : AbstractPatch
{
    private static FleaRestrictions _restrictions = null!;

    protected override MethodBase GetTargetMethod()
    {
        _restrictions = restrictions;
        return AccessTools.Method(
            typeof(RagfairController),
            nameof(RagfairController.AddPlayerOffer)
        );
    }

    [PatchPrefix]
    private static bool Prefix(MongoId sessionID, ref ItemEventRouterResponse __result)
    {
        if (!FleaRestrictions.Active(sessionID))
            return true;
        __result = _restrictions.Reject(sessionID);
        return false;
    }
}
