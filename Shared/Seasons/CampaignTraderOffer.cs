using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using WTT.Campaigns.Shared.Native;
using WTT.Campaigns.Shared.Serialization;

namespace WTT.Campaigns.Shared.Seasons;

public sealed class CampaignTraderAssort : ExtensibleJsonModel
{
    public string TraderId { get; set; } = "";
    public bool ReplaceExisting { get; set; }
    public List<string> RemovedOffers { get; set; } = new();

    public bool AllowsInstalled(string offerId) => !ReplaceExisting && !RemovedOffers.Contains(offerId);
}

public sealed class CampaignTraderOffer : ExtensibleJsonModel
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "New offer";
    public string TraderId { get; set; } = "";
    public List<NativeItem> Items { get; set; } = new();
    public List<List<NativeBarter>> Barter { get; set; } = new();
    public int Loyalty { get; set; } = 1;
    public bool UnlimitedStock { get; set; } = true;
    public int Stock { get; set; } = 100;
    public int PurchaseLimit { get; set; }
    public bool RequiresUnlock { get; set; }
    public string UnlockQuestId { get; set; } = "";

    public bool ShouldSerializeUnlockQuestId() => UnlockQuestId.Length > 0;

    public NativeReward Reference() =>
        new()
        {
            Id = Guid.NewGuid().ToString("N").Substring(0, 24),
            Type = "AssortmentUnlock",
            Target = Id,
            TraderId = TraderId,
            LoyaltyLevel = Loyalty,
        };
}

public static class TraderOfferRules
{
    public const int MaxItems = 256;
    public const string Fence = "579dc571d53a0658a154fbec";

    public static bool Eligible(
        CampaignTraderOffer offer,
        string owningCampaign,
        string? characterCampaign,
        bool unlockedTrader,
        int loyalty,
        bool claimed
    ) => owningCampaign == characterCampaign && unlockedTrader && loyalty >= offer.Loyalty && (!offer.RequiresUnlock || claimed);

    // IDs and prices do not affect a detached assembly. Stable ordering also permits
    // verification reuse after duplicating a campaign without trusting imported receipts.
    public static string AssemblyHash(IReadOnlyList<NativeItem> items)
    {
        var copy = SeasonCompiler.Copy(items.AsValueEnumerable().ToList());
        var identities = copy.AsValueEnumerable()
            .Select((item, index) => new { item.Id, Value = index.ToString() })
            .ToDictionary(p => p.Id, p => p.Value);
        foreach (var item in copy)
        {
            item.Id = identities[item.Id];
            item.ParentId = item.ParentId != null && identities.TryGetValue(item.ParentId, out var parent) ? parent : null;
            if (item.ParentId == null)
                item.SlotId = null;
        }
        using var hash = SHA256.Create();
        return BitConverter
            .ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(copy))))
            .Replace("-", "")
            .ToLowerInvariant();
    }

    public static void Validate(SeasonDefinition definition, SeasonValidationResult result)
    {
        var traders = new HashSet<string>();
        foreach (var assort in definition.TraderAssorts)
        {
            var path = "Trader offers/" + assort.TraderId;
            if (
                definition.FormatVersion is not (3 or 4 or 5 or 6 or 7 or 8 or 9 or 10 or 11 or 12)
                || !SeasonValidator.IsId(assort.TraderId)
                || assort.TraderId == Fence
                || !traders.Add(assort.TraderId)
            )
                result.Add(path, "Assortment changes require format 3 and a unique fixed-assort trader.");
            if (
                assort.RemovedOffers.AsValueEnumerable().Any(id => !SeasonValidator.IsId(id))
                || assort.RemovedOffers.AsValueEnumerable().Distinct().Count() != assort.RemovedOffers.Count
            )
                result.Add(path, "Removed offer identities must be valid and unique.");
            foreach (
                var grant in definition
                    .AllRewards.AsValueEnumerable()
                    .SelectMany(r => r.Grants)
                    .Where(g => g.Type == "AssortmentUnlock" && g.TraderId == assort.TraderId)
            )
                if (
                    !definition.TraderOffers.AsValueEnumerable().Any(o => o.Id == grant.Target)
                    && !assort.AllowsInstalled(grant.Target ?? "")
                )
                    result.Add(path, "A reward still references an installed offer removed from this assortment.");
        }
        var ids = new HashSet<string>();
        foreach (var offer in definition.TraderOffers)
        {
            var path = "Trader offers/" + offer.Id;
            void Need(bool valid, string message)
            {
                if (!valid)
                    result.Add(path, message);
            }
            Need(definition.FormatVersion is 3 or 4 or 5 or 6 or 7 or 8 or 9 or 10 or 11 or 12, "Trader offers require campaign format 3.");
            Need(
                SeasonValidator.IsId(offer.Id) && SeasonValidator.IsId(offer.TraderId) && offer.TraderId != Fence,
                "Choose a fixed-assort trader and valid offer identity."
            );
            Need(offer.Items.Count <= MaxItems, "An assembly supports at most 256 items.");
            SeasonValidator.ItemTree(offer.Items, path, result);
            Need(
                offer.Items.AsValueEnumerable().Count(i => i.Id == offer.Id && (i.ParentId == null || i.ParentId == "hideout")) == 1,
                "Offer identity must match its root item."
            );
            foreach (var item in offer.Items)
            {
                Need(ids.Add(item.Id), "Item identities must be unique across campaign offers.");
                var quantity = item.Upd?.StackObjectsCount ?? 1;
                Need(
                    !double.IsNaN(quantity)
                        && !double.IsInfinity(quantity)
                        && quantity >= 1
                        && quantity <= 10000000
                        && Math.Truncate(quantity) == quantity,
                    "Item quantities must be positive whole numbers."
                );
                Need(item.Id == offer.Id || !string.IsNullOrEmpty(item.SlotId), "Choose a destination slot for every child item.");
                if (item.Location?.Grid is { } grid)
                    Need(grid.X >= 0 && grid.Y >= 0, "Grid coordinates cannot be negative.");
                if (item.Location?.Slot is { } slot)
                    Need(slot >= 0, "Ammunition positions cannot be negative.");
            }
            Need(offer.Loyalty is >= 1 and <= 4, "Loyalty must be between 1 and 4.");
            Need(
                offer.Stock is >= 1 and <= 10000000 && offer.PurchaseLimit is >= 0 and <= 10000000,
                "Stock and purchase limit are outside supported bounds."
            );
            Need(
                offer.Barter.Count is > 0 and <= 16
                    && offer
                        .Barter.AsValueEnumerable()
                        .All(b =>
                            b.Count is > 0 and <= 32
                            && b.AsValueEnumerable()
                                .All(c =>
                                    SeasonValidator.IsId(c.Template)
                                    && c.Count > 0
                                    && c.Count <= 1000000000
                                    && Math.Truncate(c.Count) == c.Count
                                )
                        ),
                "Each payment alternative needs positive whole-number costs."
            );
            if (offer.RequiresUnlock)
                Need(
                    definition
                        .AllRewards.AsValueEnumerable()
                        .Any(r => r.Enabled && r.Grants.AsValueEnumerable().Any(g => g.Type == "AssortmentUnlock" && g.Target == offer.Id))
                        || definition
                            .Quests.AsValueEnumerable()
                            .Any(q =>
                                q.SeasonalEnabled != false
                                && q.Id == offer.UnlockQuestId
                                && (q.Rewards.GetValueOrDefault("Success") ?? new())
                                    .AsValueEnumerable()
                                    .Any(g => g.Type == "AssortmentUnlock" && g.Target == offer.Id)
                            ),
                    "An unlock-only offer needs an enabled reward that unlocks it."
                );
            if (offer.UnlockQuestId.Length > 0)
                Need(
                    offer.RequiresUnlock
                        && definition.Quests.AsValueEnumerable().Any(q => q.Id == offer.UnlockQuestId && q.SeasonalEnabled != false),
                    "A quest offer requires an active owned quest and an unlock gate."
                );
        }
        foreach (var grant in definition.AllRewards.AsValueEnumerable().SelectMany(r => r.Grants).Where(g => g.Type == "AssortmentUnlock"))
        {
            var offer = definition.TraderOffers.AsValueEnumerable().FirstOrDefault(o => o.Id == grant.Target);
            if (offer != null && (grant.TraderId != offer.TraderId || grant.Items.Count != 0))
                result.Add(
                    "Trader offers/" + offer.Id,
                    "Authored offer rewards must reference the canonical offer without copying its items."
                );
        }
    }
}
