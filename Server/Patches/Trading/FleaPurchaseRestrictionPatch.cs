using System.Reflection;
using HarmonyLib;
using SeasonalPerks.Server.Effects;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Eft.Trade;
using SPTarkov.Server.Core.Services.Ragfair;

namespace SeasonalPerks.Server.Patches.Trading;

[Injectable]
public class FleaPurchaseRestrictionPatch(FleaRestrictions restrictions, RagfairOfferService offers)
    : AbstractPatch
{
    private static FleaRestrictions _restrictions = null!;
    private static RagfairOfferService _offers = null!;

    protected override MethodBase GetTargetMethod()
    {
        _restrictions = restrictions;
        _offers = offers;
        return AccessTools.Method(
            typeof(TradeController),
            nameof(TradeController.ConfirmRagfairTrading)
        );
    }

    [PatchPrefix]
    private static bool Prefix(
        MongoId sessionID,
        ProcessRagfairTradeRequestData request,
        ref ItemEventRouterResponse __result
    )
    {
        if (!FleaRestrictions.Active(sessionID))
            return true;
        // Preflight the entire basket: a disallowed later offer must not leave an
        // earlier trader purchase committed. Recheck live offers, never client ownership.
        foreach (var requested in request.Offers ?? [])
        {
            var offer = _offers.GetOfferByOfferId(new MongoId(requested.Id));
            if (offer == null || !offer.IsTraderOffer())
            {
                __result = _restrictions.Reject(sessionID);
                return false;
            }
        }
        return true;
    }
}
