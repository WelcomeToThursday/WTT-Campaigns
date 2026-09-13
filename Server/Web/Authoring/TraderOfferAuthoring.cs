using WTT.Campaigns.Shared.Authoring;
using WTT.Campaigns.Shared.Native;
using WTT.Campaigns.Shared.Seasons;

namespace WTT.Campaigns.Server.Web.Authoring;

public static class TraderOfferAuthoring
{
    private static string Id() => Guid.NewGuid().ToString("N")[..24];

    public static CampaignTraderAssort Assortment(SeasonDefinition season, string trader)
    {
        if (!SeasonValidator.IsId(trader) || trader == TraderOfferRules.Fence)
            throw new InvalidOperationException("Choose a trader with a fixed assortment.");
        var policy = season.TraderAssorts.FirstOrDefault(a => a.TraderId == trader);
        if (policy == null)
        {
            policy = new() { TraderId = trader };
            season.TraderAssorts.Add(policy);
        }
        season.FormatVersion = 3;
        return policy;
    }

    public static CampaignTraderOffer CopyInstalled(SeasonDefinition season, CampaignTraderOffer source, bool replace)
    {
        var offer = Create(season, source.Items, source.Name, source.TraderId);
        offer.Barter = SeasonCompiler.Copy(source.Barter);
        offer.Loyalty = source.Loyalty;
        offer.Stock = source.Stock;
        offer.UnlimitedStock = source.UnlimitedStock;
        offer.PurchaseLimit = source.PurchaseLimit;
        if (replace)
        {
            var policy = Assortment(season, source.TraderId);
            if (!policy.RemovedOffers.Contains(source.Id))
                policy.RemovedOffers.Add(source.Id);
            foreach (
                var grant in season
                    .AllRewards.SelectMany(r => r.Grants)
                    .Where(g => g.Type == "AssortmentUnlock" && g.TraderId == source.TraderId && g.Target == source.Id)
            )
            {
                grant.Target = offer.Id;
                grant.Items.Clear();
                offer.RequiresUnlock = true;
            }
        }
        return offer;
    }

    public static void RemoveInstalled(SeasonDefinition season, string trader, string id)
    {
        if (season.AllRewards.Any(r => r.Grants.Any(g => g.Type == "AssortmentUnlock" && g.TraderId == trader && g.Target == id)))
            throw new InvalidOperationException("Remove this offer's reward references before deleting it.");
        var policy = Assortment(season, trader);
        if (!policy.RemovedOffers.Contains(id))
            policy.RemovedOffers.Add(id);
    }

    public static void ClearAssortment(SeasonDefinition season, string trader, bool restoreInstalled = false)
    {
        if (season.AllRewards.Any(r => r.Grants.Any(g => g.Type == "AssortmentUnlock" && g.TraderId == trader)))
            throw new InvalidOperationException("Remove this trader's offer reward references before clearing its assortment.");
        season.TraderOffers.RemoveAll(o => o.TraderId == trader);
        var policy = Assortment(season, trader);
        policy.ReplaceExisting = !restoreInstalled;
        policy.RemovedOffers.Clear();
        if (restoreInstalled)
            season.TraderAssorts.Remove(policy);
    }

    public static void ImportAssortment(SeasonDefinition season, string trader, IReadOnlyList<CampaignTraderOffer> sources)
    {
        // Prepare all copies first; one malformed installed tree must not leave a partial import.
        var work = new SeasonDefinition();
        foreach (var source in sources)
        {
            var copy = CopyInstalled(work, source, false);
            // Standalone exports replace an existing assort.json. Keep installed
            // identities so external questassort references still resolve.
            var originalIds = copy
                .Items.Select((item, index) => (item.Id, Original: source.Items[index].Id))
                .ToDictionary(p => p.Id, p => p.Original);
            foreach (var item in copy.Items)
            {
                item.Id = originalIds[item.Id];
                if (item.ParentId != null && originalIds.TryGetValue(item.ParentId, out var parent))
                    item.ParentId = parent;
            }
            copy.Id = source.Id;
        }
        ClearAssortment(season, trader);
        season.TraderOffers.AddRange(work.TraderOffers);
    }

    public static List<NativeItem> PreviewAssembly(List<NativeItem> source)
    {
        var validation = new SeasonValidationResult();
        SeasonValidator.ItemTree(source, "Source", validation);
        if (!validation.CanPublish)
            throw new InvalidOperationException(validation.Issues[0].Message);
        var items = SeasonCompiler.Copy(source);
        var ids = items.Select(i => i.Id).ToHashSet();
        var root = items.Single(i => i.ParentId == null || !ids.Contains(i.ParentId));
        // Trader roots point at "hideout", which is not a native MongoId.
        // Render one detached item, without stock or purchase counters.
        root.ParentId = null;
        root.SlotId = null;
        root.Location = null;
        root.Upd ??= new();
        root.Upd.StackObjectsCount = 1;
        root.Upd.UnlimitedCount = null;
        root.Upd.BuyRestrictionMax = root.Upd.BuyRestrictionCurrent = null;
        return items;
    }

    public static CampaignTraderOffer Create(SeasonDefinition season, List<NativeItem> source, string name, string trader = "")
    {
        var items = PreviewAssembly(source);
        var ids = items.ToDictionary(i => i.Id, _ => Id());
        var root = items.Single(i => i.ParentId == null);
        foreach (var item in items)
        {
            item.Id = ids[item.Id];
            item.ParentId = item.ParentId != null ? ids[item.ParentId] : null;
        }
        var offer = new CampaignTraderOffer
        {
            Id = root.Id,
            Items = items,
            Name = name,
            TraderId = trader,
            Barter =
            [
                [new NativeBarter { Template = "5449016a4bdc2d6f028b456f", Count = 1000 }],
            ],
        };
        season.TraderOffers.Add(offer);
        season.FormatVersion = 3;
        return offer;
    }

    public static NativeItem Add(
        CampaignTraderOffer offer,
        string parent,
        OfferContainer container,
        string template,
        int x = 0,
        int y = 0,
        bool rotated = false
    )
    {
        if (offer.Items.Count >= TraderOfferRules.MaxItems)
            throw new InvalidOperationException("An offer supports at most 256 items.");
        if (container.Kind == "Slot" && offer.Items.Any(i => i.ParentId == parent && i.SlotId == container.Id))
            throw new InvalidOperationException("Remove the existing attachment before adding another.");
        var item = new NativeItem
        {
            Id = Id(),
            Template = template,
            ParentId = parent,
            SlotId = container.Id,
            Upd = new() { StackObjectsCount = 1 },
        };
        if (container.Kind == "Grid")
            item.Location = new(
                new NativeGridLocation
                {
                    X = x,
                    Y = y,
                    Rotation = rotated ? "1" : "0",
                    IsSearched = true,
                }
            );
        if (container.Kind == "Ammo")
            item.Location = new(offer.Items.Count(i => i.ParentId == parent && i.SlotId == container.Id));
        offer.Items.Add(item);
        return item;
    }

    public static void Remove(CampaignTraderOffer offer, string id)
    {
        if (id == offer.Id)
            throw new InvalidOperationException("Delete the offer to remove its root item.");
        var remove = new HashSet<string> { id };
        while (true)
        {
            var count = remove.Count;
            foreach (var item in offer.Items)
                if (item.ParentId != null && remove.Contains(item.ParentId))
                    remove.Add(item.Id);
            if (count == remove.Count)
                break;
        }
        offer.Items.RemoveAll(i => remove.Contains(i.Id));
        foreach (var group in offer.Items.Where(i => i.Location?.Slot != null).GroupBy(i => (i.ParentId, i.SlotId)))
        {
            var index = 0;
            foreach (var item in group.OrderBy(i => i.Location!.Slot))
                item.Location = new(index++);
        }
    }

    public static void Delete(SeasonDefinition season, CampaignTraderOffer offer)
    {
        if (season.AllRewards.Any(r => r.Grants.Any(g => g.Type == "AssortmentUnlock" && g.Target == offer.Id)))
            throw new InvalidOperationException("Remove this offer's reward references before deleting it.");
        season.TraderOffers.Remove(offer);
    }

    public static void SynchronizeReferences(SeasonDefinition season)
    {
        foreach (var grant in season.AllRewards.SelectMany(r => r.Grants).Where(g => g.Type == "AssortmentUnlock"))
        {
            var offer = season.TraderOffers.FirstOrDefault(o => o.Id == grant.Target);
            if (offer == null)
                continue;
            grant.TraderId = offer.TraderId;
            grant.LoyaltyLevel = offer.Loyalty;
            grant.Items.Clear();
        }
    }
}
