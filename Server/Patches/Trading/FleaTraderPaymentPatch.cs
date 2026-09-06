using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using SeasonalPerks.Server.Effects;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Eft.Ragfair;
using SPTarkov.Server.Core.Models.Eft.Trade;

namespace SeasonalPerks.Server.Patches.Trading;

[Injectable]
public class FleaTraderPaymentPatch(TraderPaymentValidation validation) : AbstractPatch
{
    private static TraderPaymentValidation _validation = null!;

    protected override MethodBase GetTargetMethod()
    {
        _validation = validation;
        return AccessTools.Method(typeof(TradeController), "BuyTraderItemFromRagfair");
    }

    // This caller decrements flea stock after BuyItem even on a warning. Reject here
    // as well as at BuyItem so a stale flea payment cannot consume shared stock.
    [PatchPrefix]
    [UsedImplicitly]
    private static bool Prefix(
        MongoId sessionId,
        PmcData pmcData,
        RagfairOffer fleaOffer,
        OfferRequest requestOffer,
        ItemEventRouterResponse output,
        out Lock? __state
    )
    {
        __state = null;
        __state = TraderPaymentValidation.Begin();
        return _validation.Validate(
            pmcData,
            new ProcessBuyTradeRequestData
            {
                Action = "TradingConfirm",
                Type = "buy_from_ragfair_trader",
                TransactionId = fleaOffer.User.Id,
                ItemId = fleaOffer.Root,
                Count = requestOffer.Count,
                SchemeId = 0,
                SchemeItems = requestOffer.Items,
            },
            sessionId,
            output
        );
    }

    [PatchFinalizer]
    [UsedImplicitly]
    private static void Finalizer(Lock? __state)
    {
        __state?.Exit();
    }
}
