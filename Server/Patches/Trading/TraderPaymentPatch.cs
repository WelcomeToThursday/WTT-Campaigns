using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Helpers.Commerce;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Eft.Trade;
using WTT.Campaigns.Server.Effects;

namespace WTT.Campaigns.Server.Patches.Trading;

[Injectable]
public class TraderPaymentPatch(TraderPaymentValidation validation) : AbstractPatch
{
    private static TraderPaymentValidation _validation = null!;

    protected override MethodBase GetTargetMethod()
    {
        _validation = validation;
        return AccessTools.Method(typeof(TradeHelper), nameof(TradeHelper.BuyItem));
    }

    [PatchPrefix]
    [UsedImplicitly]
    private static bool Prefix(
        PmcData pmcData,
        ProcessBuyTradeRequestData buyRequestData,
        MongoId sessionId,
        ItemEventRouterResponse output,
        out Lock? __state
    )
    {
        __state = null;
        __state = TraderPaymentValidation.Begin();
        return _validation.Validate(pmcData, buyRequestData, sessionId, output);
    }

    [PatchFinalizer]
    [UsedImplicitly]
    private static void Finalizer(Lock? __state)
    {
        __state?.Exit();
    }
}
