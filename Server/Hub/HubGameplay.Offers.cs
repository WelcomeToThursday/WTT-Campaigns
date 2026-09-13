using Newtonsoft.Json;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Ragfair;
using WTT.Campaigns.Server.Seasons;
using WTT.Campaigns.Shared.Seasons;

namespace WTT.Campaigns.Server.Hub;

public sealed partial class HubGameplay
{
    private readonly HashSet<string> _fallbackOffers = new();

    private void ResolveOffers()
    {
        foreach (var grant in _catalogue.Rewards.Values.SelectMany(r => r.Grants).Where(g => (string?)g.Type == "AssortmentUnlock"))
        {
            var target = (string)grant.Target!;
            if (_catalogue.TraderOffers.Any(o => o.Id == target))
                continue;
            var traderId = new MongoId((string)grant.TraderId!);
            if (!traders.TryGetValue(traderId, out var trader) || trader.Assort == null)
            {
                continue;
            }

            var items = json.Deserialize<List<Item>>(JsonConvert.SerializeObject(grant.Items))!;
            var sourceRoot = items.Single(i => i.Id.ToString() == target);
            var matches = trader
                .Assort.Items.Where(i =>
                    i.Template == sourceRoot.Template
                    && trader.Assort.BarterScheme.ContainsKey(i.Id)
                    && trader.Assort.LoyalLevelItems.GetValueOrDefault(i.Id) == (int)grant.LoyaltyLevel!
                    && Signature(items, target) == Signature(trader.Assort.Items, i.Id.ToString())
                )
                .ToArray();
            if (matches.Length == 1)
            {
                var offer = matches[0];
                _offerIds[target] = offer.Id.ToString();
                _offers[target] = CopyOffer(trader.Assort, offer.Id);
                continue;
            }
            if (matches.Length > 1)
            {
                continue;
            }

            var fallback = _catalogue.Offers.FirstOrDefault(o => (string?)o.Target == target);
            if (fallback == null)
            {
                continue;
            }

            var assortment = new TraderAssort
            {
                Items = items,
                BarterScheme = new()
                {
                    [new MongoId(target)] = json.Deserialize<List<List<BarterScheme>>>(JsonConvert.SerializeObject(fallback.Barter))!,
                },
                LoyalLevelItems = new() { [new MongoId(target)] = fallback.Loyalty },
            };
            var root = assortment.Items.Single(i => i.Id.ToString() == target);
            root.ParentId = "hideout";
            root.SlotId = "hideout";
            root.Upd ??= new Upd();
            root.Upd.StackObjectsCount = 999999;
            root.Upd.UnlimitedCount = true;
            if (
                assortment.Items.Any(i => !templates.Items.ContainsKey(i.Template))
                || assortment.BarterScheme.Values.SelectMany(v => v).SelectMany(v => v).Any(c => !templates.Items.ContainsKey(c.Template))
            )
            {
                continue;
            }

            _offerIds[target] = target;
            _offers[target] = assortment;
            _fallbackOffers.Add(target);
            // Native purchase callbacks resolve stock from this table. Visibility remains profile-specific below.
            trader.Assort.Items.AddRange(cloner.Clone(assortment.Items)!);
            trader.Assort.BarterScheme[root.Id] = cloner.Clone(assortment.BarterScheme[root.Id])!;
            trader.Assort.LoyalLevelItems[root.Id] = assortment.LoyalLevelItems[root.Id];
        }
    }

    internal static string Signature(IEnumerable<Item> items, string root)
    {
        var all = items.ToDictionary(i => i.Id.ToString());
        var parts = new List<string>();
        foreach (var item in all.Values)
        {
            var path = item.Template.ToString();
            var current = item;
            var seen = new HashSet<string>();
            while (
                current.Id.ToString() != root
                && current.ParentId != null
                && seen.Add(current.Id.ToString())
                && all.TryGetValue(current.ParentId, out var parent)
            )
            {
                path = current.SlotId + "/" + path;
                current = parent;
            }
            if (current.Id.ToString() == root)
            {
                parts.Add(path);
            }
        }
        return string.Join("|", parts.OrderBy(p => p, StringComparer.Ordinal));
    }

    private TraderAssort CopyOffer(TraderAssort source, MongoId root)
    {
        var copy = cloner.Clone(source)!;
        var keep = Descendants(copy.Items, root.ToString());
        copy.Items = copy.Items.Where(i => keep.Contains(i.Id.ToString())).ToList();
        copy.BarterScheme = copy.BarterScheme.Where(p => p.Key == root).ToDictionary();
        copy.LoyalLevelItems = copy.LoyalLevelItems.Where(p => p.Key == root).ToDictionary();
        return copy;
    }

    private static HashSet<string> Descendants(IEnumerable<Item> items, string root)
    {
        var all = items.ToList();
        var keep = new HashSet<string> { root };
        bool changed;
        do
        {
            changed = false;
            foreach (var item in all)
            {
                if (item.ParentId != null && keep.Contains(item.ParentId))
                {
                    changed |= keep.Add(item.Id.ToString());
                }
            }
        } while (changed);
        return keep;
    }

    public TraderAssort FilterOffers(string sessionId, string traderId, TraderAssort original)
    {
        if (_runtimes != null)
        {
            foreach (var runtime in _runtimes.Values)
            {
                original = runtime.FilterOffers(sessionId, traderId, original);
            }

            return original;
        }
        if (!_ready)
        {
            return original;
        }

        var result = cloner.Clone(original)!;
        if (
            seasons.IsSeasonal(sessionId)
            && seasons.SeasonIdFor(saves.GetProfile(new MongoId(sessionId)).CharacterData!.PmcData!) == _presentation.SeasonId
        )
        {
            var policy = _catalogue.TraderAssorts.FirstOrDefault(a => a.TraderId == traderId);
            if (policy != null)
                CampaignTraderStock.FilterInstalled(result, policy, _catalogue.TraderOffers.Select(o => o.Id).ToHashSet());
        }
        foreach (var offer in _catalogue.TraderOffers.Where(o => o.TraderId == traderId))
        {
            if ((_manager ?? this).OfferAllowed(sessionId, offer.Id))
                continue;
            var remove = Descendants(result.Items, offer.Id);
            result.Items.RemoveAll(i => remove.Contains(i.Id.ToString()));
            result.BarterScheme.Remove(new MongoId(offer.Id));
            result.LoyalLevelItems.Remove(new MongoId(offer.Id));
        }
        foreach (
            var grant in _catalogue
                .Rewards.Values.SelectMany(r => r.Grants)
                .Where(g => (string?)g.Type == "AssortmentUnlock" && (string?)g.TraderId == traderId)
        )
        {
            var target = (string)grant.Target!;
            if (!_offerIds.TryGetValue(target, out var mapped) || (_manager ?? this).OfferAllowed(sessionId, mapped))
            {
                continue;
            }

            var remove = Descendants(result.Items, mapped);
            result.Items = result.Items.Where(i => !remove.Contains(i.Id.ToString())).ToList();
            result.BarterScheme.Remove(new MongoId(mapped));
            result.LoyalLevelItems.Remove(new MongoId(mapped));
        }
        return result;
    }

    public bool OfferAllowed(string sessionId, string offerId)
    {
        if (_runtimes != null)
        {
            var owner = _runtimes.Values.FirstOrDefault(r => r._catalogue.TraderOffers.Any(o => o.Id == offerId));
            if (owner != null)
                return owner.AuthoredOfferAllowed(sessionId, offerId);
        }
        else if (_catalogue.TraderOffers.Any(o => o.Id == offerId))
            return AuthoredOfferAllowed(sessionId, offerId);
        if (_runtimes != null)
        {
            if (seasons.IsSeasonal(sessionId))
            {
                var activePmc = saves.GetProfile(new MongoId(sessionId)).CharacterData!.PmcData!;
                if (
                    _runtimes.TryGetValue(seasons.SeasonIdFor(activePmc), out var policyOwner)
                    && !policyOwner.InstalledOfferAllowed(offerId)
                )
                    return false;
                if (_runtimes.TryGetValue(seasons.SeasonIdFor(activePmc), out var active) && active._offerIds.ContainsValue(offerId))
                {
                    return active.OfferAllowed(sessionId, offerId);
                }
            }
            return _runtimes.Values.All(r => r.OfferAllowed(sessionId, offerId));
        }

        if (!_ready)
        {
            return true;
        }
        if (
            _catalogue.TraderAssorts.Count > 0
            && seasons.IsSeasonal(sessionId)
            && seasons.SeasonIdFor(saves.GetProfile(new MongoId(sessionId)).CharacterData!.PmcData!) == _presentation.SeasonId
            && !InstalledOfferAllowed(offerId)
        )
            return false;
        var targets = _offerIds.Where(p => p.Value == offerId).Select(p => p.Key).ToArray();
        if (targets.Length == 0)
        {
            return true;
        }
        if (!seasons.IsSeasonal(sessionId))
        {
            return !targets.Any(_fallbackOffers.Contains);
        }
        var pmc = saves.GetProfile(new MongoId(sessionId)).CharacterData!.PmcData!;
        if (seasons.SeasonIdFor(pmc) != _presentation.SeasonId)
        {
            return !targets.Any(_fallbackOffers.Contains);
        }

        var progress = Progress(pmc);
        return targets.Any(progress.UnlockedOffers.Contains);
    }

    private bool InstalledOfferAllowed(string offerId)
    {
        if (!SeasonValidator.IsId(offerId))
            return false;
        foreach (var policy in _catalogue.TraderAssorts)
        {
            if (policy.RemovedOffers.Contains(offerId))
                return false;
            if (
                policy.ReplaceExisting
                && traders.TryGetValue(new MongoId(policy.TraderId), out var trader)
                && trader.Assort?.BarterScheme.ContainsKey(new MongoId(offerId)) == true
            )
                return false;
        }
        return true;
    }

    public void RestoreFallbackOffers(string traderId)
    {
        if (_runtimes != null)
        {
            foreach (var runtime in _runtimes.Values)
            {
                runtime.RestoreFallbackOffers(traderId);
            }

            return;
        }
        if (!_ready || !traders.TryGetValue(new MongoId(traderId), out var trader) || trader.Assort == null)
        {
            return;
        }
        lock (_offers)
        {
            foreach (var target in _fallbackOffers)
            {
                var definition = _catalogue.Rewards.Values.SelectMany(r => r.Grants).First(g => (string?)g.Target == target);
                var id = new MongoId(target);
                if ((string?)definition.TraderId != traderId || trader.Assort.Items.Any(i => i.Id == id))
                {
                    continue;
                }
                // Native restocks can restore a cached assortment created before late content validation.
                var offer = cloner.Clone(_offers[target])!;
                trader.Assort.Items.AddRange(offer.Items);
                trader.Assort.BarterScheme[id] = offer.BarterScheme[id];
                trader.Assort.LoyalLevelItems[id] = offer.LoyalLevelItems[id];
            }
        }
    }

    public bool FleaOfferAllowed(string sessionId, RagfairOffer offer)
    {
        return !offer.IsTraderOffer() || offer.Items?.FirstOrDefault() is { } item && OfferAllowed(sessionId, item.Id.ToString());
    }
}
