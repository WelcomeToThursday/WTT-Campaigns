using Newtonsoft.Json;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using WTT.Campaigns.Server.Seasons;
using WTT.Campaigns.Shared.Seasons;

namespace WTT.Campaigns.Server.Hub;

public sealed partial class HubGameplay
{
    private void RegisterAuthoredOffers()
    {
        foreach (var offer in _catalogue.TraderOffers)
        {
            if (!traders.TryGetValue(new MongoId(offer.TraderId), out var trader) || trader.Assort == null)
                throw new InvalidDataException("Campaign trader is missing: " + offer.TraderId);
            if (traders.Values.Any(t => t.Assort?.Items.Any(i => offer.Items.Any(n => n.Id == i.Id.ToString())) == true))
                throw new InvalidDataException("Campaign offer collides with another installed offer: " + offer.Id);
            var assort = CampaignTraderStock.Create(offer, json);
            var root = new MongoId(offer.Id);
            _offerIds[offer.Id] = offer.Id;
            _offers[offer.Id] = cloner.Clone(assort)!;
            trader.Assort.Items.AddRange(assort.Items);
            trader.Assort.BarterScheme[root] = assort.BarterScheme[root];
            trader.Assort.LoyalLevelItems[root] = offer.Loyalty;
        }
    }

    private bool AuthoredOfferAllowed(string sessionId, string offerId)
    {
        if (!seasons.IsSeasonal(sessionId))
            return false;
        var pmc = saves.GetProfile(new MongoId(sessionId)).CharacterData!.PmcData!;
        if (seasons.SeasonIdFor(pmc) != _presentation.SeasonId)
            return false;
        var offer = _catalogue.TraderOffers.Single(o => o.Id == offerId);
        // Native GetAssort performs loyalty/access filtering; retain a purchase-side
        // gate as well, including requests with a guessed assort ID.
        if (
            pmc.TradersInfo == null
            || !pmc.TradersInfo.TryGetValue(new MongoId(offer.TraderId), out var info)
            || info.Unlocked != true
            || info.LoyaltyLevel < offer.Loyalty
        )
            return false;
        return TraderOfferRules.Eligible(
            offer,
            _presentation.SeasonId,
            seasons.SeasonIdFor(pmc),
            info.Unlocked == true,
            (int)(info.LoyaltyLevel ?? 0),
            offer.UnlockQuestId.Length > 0
                ? pmc.Quests?.Any(q =>
                    q.QId.ToString() == offer.UnlockQuestId && q.Status == SPTarkov.Server.Core.Models.Enums.QuestStatusEnum.Success
                ) == true
                : Progress(pmc).UnlockedOffers.Contains(offer.Id)
        );
    }

    public void RestockAuthoredOffers(Trader trader)
    {
        if (_runtimes != null)
        {
            foreach (var runtime in _runtimes.Values)
                runtime.RestockAuthoredOffers(trader);
            return;
        }
        if (!_ready || trader.Assort == null)
            return;
        foreach (var offer in _catalogue.TraderOffers.Where(o => o.TraderId == trader.Base.Id.ToString()))
        {
            CampaignTraderStock.Restore(trader.Assort, offer, json);
        }
    }
}
