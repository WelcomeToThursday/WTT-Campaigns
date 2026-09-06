using HarmonyLib;
using SeasonalPerks.Shared;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.Commerce;
using SPTarkov.Server.Core.Helpers.Traders;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Eft.Trade;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Utils;

namespace SeasonalPerks.Server.Effects;

[Injectable(InjectionType.Singleton)]
public sealed class TraderPaymentValidation(
    TraderAssortHelper assorts,
    PaymentHelper payment,
    HttpResponseUtil responses
)
{
    // Validate under the same reentrant lock that native BuyItem holds while granting
    // items and charging payment. Concurrent requests must not validate stale funds.
    private static readonly Lock BuyLock = (Lock)
        AccessTools.Field(typeof(TradeHelper), "_buyLock").GetValue(null)!;

    internal static Lock Begin()
    {
        BuyLock.Enter();
        return BuyLock;
    }

    internal bool Validate(
        PmcData pmc,
        ProcessBuyTradeRequestData request,
        MongoId session,
        ItemEventRouterResponse output
    )
    {
        if (request.Type == "buy_from_ragfair_pmc")
            return true;
        var effects = new RuntimeEffects(
            ServerStartup.Seasons.Catalogue,
            SeasonService.State(pmc).SeasonalPerks
        );
        if (effects.TraderMultiplier(request.TransactionId.ToString(), "buy") == 1m)
            return true;

        bool Reject()
        {
            responses.AppendErrorToOutput(
                output,
                "Trader price or payment changed. Refresh the offer and try again.",
                BackendErrorCodes.UnknownTradingError
            );
            return false;
        }

        if (request.Count is not > 0 || request.SchemeId is not >= 0 || request.SchemeItems == null)
            return Reject();
        var inventory = pmc.Inventory?.Items;
        if (inventory == null)
            return Reject();
        var assort = assorts.GetAssort(session, request.TransactionId);
        if (
            !assort.BarterScheme.TryGetValue(request.ItemId, out var variants)
            || request.SchemeId.Value >= variants.Count
            || !assort.Items.Any(i => i.Id == request.ItemId)
        )
            return Reject();

        var expected = new Dictionary<MongoId, double>();
        foreach (var requirement in variants[request.SchemeId.Value])
        {
            if (requirement.Count is not > 0 || !double.IsFinite(requirement.Count.Value))
                return Reject();
            var count = TraderPricing.Required(requirement.Count.Value, request.Count.Value);
            expected[requirement.Template] =
                expected.GetValueOrDefault(requirement.Template) + count;
        }
        var supplied = new Dictionary<MongoId, double>();
        var stacks = new Dictionary<MongoId, double>();
        foreach (var entry in request.SchemeItems)
        {
            if (
                entry.Count is not > 0
                || !double.IsFinite(entry.Count.Value)
                || Math.Truncate(entry.Count.Value) != entry.Count.Value
            )
                return Reject();
            var item = inventory.FirstOrDefault(i => i.Id == entry.Id);
            // SPT also accepts a currency template ID and chooses stacks itself.
            var template = item?.Template ?? entry.Id;
            if (item == null && !payment.IsMoneyTpl(template))
                return Reject();
            supplied[template] = supplied.GetValueOrDefault(template) + entry.Count.Value;
            if (item != null)
            {
                stacks[item.Id] = stacks.GetValueOrDefault(item.Id) + entry.Count.Value;
                if (stacks[item.Id] > (item.Upd?.StackObjectsCount ?? 1))
                    return Reject();
            }
        }
        if (
            expected.Count == 0
            || supplied.Count != expected.Count
            || expected.Any(e => supplied.GetValueOrDefault(e.Key) != e.Value)
        )
            return Reject();
        // Currency-template payments can draw from multiple stacks. Check total funds
        // before native BuyItem grants items and decrements trader stock.
        foreach (var entry in supplied.Where(e => payment.IsMoneyTpl(e.Key)))
            if (
                inventory
                    .Where(i => i.Template == entry.Key)
                    .Sum(i => i.Upd?.StackObjectsCount ?? 1) < entry.Value
            )
                return Reject();
        return true;
    }
}
