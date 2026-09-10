using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using WTT.Campaigns.Server.Effects;

namespace WTT.Campaigns.Server.Patches.Trading;

[Injectable]
public class FleaExtendRestrictionPatch(FleaRestrictions restrictions) : AbstractPatch
{
    private static FleaRestrictions _restrictions = null!;

    protected override MethodBase GetTargetMethod()
    {
        _restrictions = restrictions;
        return AccessTools.Method(typeof(RagfairController), nameof(RagfairController.ExtendOffer));
    }

    [PatchPrefix]
    [UsedImplicitly]
    private static bool Prefix(MongoId sessionId, ref ItemEventRouterResponse __result)
    {
        if (!FleaRestrictions.Active(sessionId))
        {
            return true;
        }

        __result = _restrictions.Reject(sessionId);
        return false;
    }
}
