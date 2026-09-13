using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Utils;
using WTT.Campaigns.Server.Seasons;
using WTT.Campaigns.Server.Web.Authoring;
using WTT.Campaigns.Shared.Native;
using WTT.Campaigns.Shared.Seasons;

namespace WTT.Campaigns.Tests;

internal static class AssortmentEditingChecks
{
    internal static void Run(
        SeasonRepository repository,
        TraderOfferCatalogue catalogue,
        CampaignTraderOffer original,
        JsonUtil json,
        Action<bool, string> check
    )
    {
        var source = SeasonCompiler.Copy(original);
        source.Barter =
        [
            [new() { Template = source.Items[0].Template, Count = 3 }],
        ];
        var imported = JObject.FromObject(source.Items[0]);
        imported["moddedField"] = new JObject { ["nested"] = 42 };
        source.Items[0] = imported.ToObject<NativeItem>()!;
        var unchanged = JsonConvert.SerializeObject(source);
        var season = repository.Create(false).Definition;
        season.SeasonalRewards.Add(
            new()
            {
                Id = "000000000000000000000077",
                Grants =
                [
                    new()
                    {
                        Type = "AssortmentUnlock",
                        Target = source.Id,
                        TraderId = source.TraderId,
                        Items = SeasonCompiler.Copy(source.Items),
                    },
                ],
            }
        );
        var replacement = TraderOfferAuthoring.CopyInstalled(season, source, true);
        check(
            replacement.Id != source.Id && season.TraderAssorts.Single().RemovedOffers.Contains(source.Id),
            "Editing installed stock records a replacement without reusing native item identities"
        );
        check(
            replacement.RequiresUnlock
                && season.AllRewards.Single().Grants.Single().Target == replacement.Id
                && season.AllRewards.Single().Grants.Single().Items.Count == 0,
            "Editing a reward-linked native offer moves its unlock to the canonical replacement"
        );
        var duplicate = repository.Create(true, season).Definition;
        check(
            duplicate.TraderAssorts.Single().RemovedOffers.Single() == source.Id
                && duplicate.AllRewards.Single().Grants.Single().Target == duplicate.TraderOffers.Single().Id
                && duplicate.TraderOffers.Single().Id != replacement.Id,
            "Duplication remaps owned replacements and reward links while retaining native source identities"
        );
        var denied = false;
        try
        {
            TraderOfferAuthoring.ClearAssortment(season, source.TraderId);
        }
        catch (InvalidOperationException)
        {
            denied = true;
        }
        check(denied && season.TraderOffers.Count == 1, "Referenced assortment clear fails without partial deletion");
        season.SeasonalRewards.Clear();
        var beforeHash = SeasonRepository.GameplayHash(season);
        TraderOfferAuthoring.ClearAssortment(season, source.TraderId);
        check(
            season.TraderOffers.Count == 0
                && season.TraderAssorts.Single().ReplaceExisting
                && SeasonRepository.GameplayHash(season) != beforeHash,
            "Clearing creates a gameplay-significant empty replacement assortment"
        );
        var stock = CampaignTraderStock.Create(source, json);
        CampaignTraderStock.FilterInstalled(stock, season.TraderAssorts.Single(), new HashSet<string>());
        check(
            stock.Items.Count == 0 && stock.BarterScheme.Count == 0 && stock.LoyalLevelItems.Count == 0,
            "Runtime assortment replacement removes native roots, descendants, prices and loyalty records together"
        );
        stock = CampaignTraderStock.Create(source, json);
        var owned = CampaignTraderStock.Create(replacement, json);
        stock.Items.AddRange(owned.Items);
        foreach (var pair in owned.BarterScheme)
            stock.BarterScheme.Add(pair.Key, pair.Value);
        foreach (var pair in owned.LoyalLevelItems)
            stock.LoyalLevelItems.Add(pair.Key, pair.Value);
        CampaignTraderStock.FilterInstalled(stock, season.TraderAssorts.Single(), new HashSet<string> { replacement.Id });
        check(
            stock.Items.Count == replacement.Items.Count && stock.BarterScheme.Count == 1,
            "A cleared trader retains newly authored campaign offers"
        );
        TraderOfferAuthoring.ClearAssortment(season, source.TraderId, true);
        check(season.TraderAssorts.Count == 0, "Restoring installed stock removes the campaign override");
        var standalone = new StandaloneAssortRepository(repository, catalogue);
        var workspace = standalone.Open();
        TraderOfferAuthoring.ImportAssortment(workspace.Definition, source.TraderId, [source]);
        var stale = SeasonCompiler.Copy(workspace);
        workspace = standalone.Save(workspace);
        check(standalone.Open().Definition.TraderOffers.Count == 1, "Standalone assortment draft persists independently");
        denied = false;
        try
        {
            standalone.Save(stale);
        }
        catch (InvalidOperationException)
        {
            denied = true;
        }
        check(denied, "A stale standalone editor cannot overwrite a newer saved revision");
        var bytes = standalone.Export(source.TraderId, workspace.Revision);
        var exported = json.Deserialize<TraderAssort>(Encoding.UTF8.GetString(bytes))!;
        var exportedRoot = exported.Items.Single(i => i.ParentId == "hideout");
        check(
            exportedRoot.Id.ToString() == source.Id
                && exported.Items.Select(i => i.Id.ToString()).ToHashSet().SetEquals(source.Items.Select(i => i.Id)),
            "Standalone export retains installed offer and child IDs for external trader references"
        );
        check(
            exported.Items.Count == source.Items.Count
                && exported.BarterScheme[exportedRoot.Id][0][0].Count == 3
                && exported.LoyalLevelItems[exportedRoot.Id] == source.Loyalty,
            "Standalone export reads as a native TraderAssort with complete item, barter and loyalty data"
        );
        check(
            exportedRoot.Upd!.StackObjectsCount == source.Stock
                && exportedRoot.Upd.BuyRestrictionCurrent == 0
                && Encoding.UTF8.GetString(bytes).Contains("moddedField"),
            "Export preserves unknown mod fields and stock settings while resetting runtime counters"
        );
        TraderOfferAuthoring.ClearAssortment(workspace.Definition, source.TraderId);
        workspace = standalone.Save(workspace);
        var empty = json.Deserialize<TraderAssort>(Encoding.UTF8.GetString(standalone.Export(source.TraderId, workspace.Revision)))!;
        check(
            empty.Items.Count == 0 && empty.BarterScheme.Count == 0,
            "An entirely wiped standalone assortment exports valid empty native stock"
        );
        check(
            JsonConvert.SerializeObject(source) == unchanged,
            "Copying, clearing and exporting never modifies the installed source assembly"
        );
    }
}
