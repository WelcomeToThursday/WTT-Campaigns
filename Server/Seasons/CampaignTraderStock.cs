using Newtonsoft.Json;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Utils;
using WTT.Campaigns.Shared.Seasons;

namespace WTT.Campaigns.Server.Seasons;

public static class CampaignTraderStock
{
    public static void FilterInstalled(TraderAssort target, CampaignTraderAssort policy, ISet<string> owned)
    {
        var removed = target
            .BarterScheme.Keys.Select(id => id.ToString())
            .Where(id => !owned.Contains(id) && !policy.AllowsInstalled(id))
            .ToHashSet();
        var roots = removed.ToArray();
        var count = -1;
        while (count != removed.Count)
        {
            count = removed.Count;
            foreach (var item in target.Items)
                if (item.ParentId != null && removed.Contains(item.ParentId))
                    removed.Add(item.Id.ToString());
        }
        target.Items.RemoveAll(i => removed.Contains(i.Id.ToString()));
        foreach (var root in roots)
        {
            target.BarterScheme.Remove(new MongoId(root));
            target.LoyalLevelItems.Remove(new MongoId(root));
        }
    }

    public static TraderAssort Create(CampaignTraderOffer offer, JsonUtil json)
    {
        var items = json.Deserialize<List<Item>>(JsonConvert.SerializeObject(offer.Items))!;
        var root = items.Single(i => i.Id.ToString() == offer.Id);
        root.ParentId = "hideout";
        root.SlotId = "hideout";
        root.Upd ??= new();
        root.Upd.StackObjectsCount = offer.Stock;
        root.Upd.UnlimitedCount = offer.UnlimitedStock;
        root.Upd.BuyRestrictionMax = offer.PurchaseLimit > 0 ? offer.PurchaseLimit : null;
        root.Upd.BuyRestrictionCurrent = 0;
        return new()
        {
            Items = items,
            BarterScheme = new() { [root.Id] = json.Deserialize<List<List<BarterScheme>>>(JsonConvert.SerializeObject(offer.Barter))! },
            LoyalLevelItems = new() { [root.Id] = offer.Loyalty },
        };
    }

    // Called only for registration and native restock, never from a shop read.
    public static void Restore(TraderAssort target, CampaignTraderOffer offer, JsonUtil json)
    {
        var original = Create(offer, json);
        var ids = original.Items.Select(i => i.Id).ToHashSet();
        target.Items.RemoveAll(i => ids.Contains(i.Id));
        target.Items.AddRange(original.Items);
        var root = new MongoId(offer.Id);
        target.BarterScheme[root] = original.BarterScheme[root];
        target.LoyalLevelItems[root] = offer.Loyalty;
    }
}
