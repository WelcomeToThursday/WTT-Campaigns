using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Items;
using SPTarkov.Server.Core.Utils.Cloners;

namespace WTT.Campaigns.Tests;

// Actual SPT validity rules against a small in-memory database; no server/profile.
internal static class NativeItemHelperFixture
{
    internal static ItemHelper Create(TemplateTable table, params MongoId[] blacklisted)
    {
        var config = new ItemConfig
        {
            RewardItemBlacklist = [],
            RewardItemTypeBlacklist = [],
            BossItems = [],
            CustomItemGlobalPresets = [],
            Blacklist = [.. blacklisted],
            LootableItemBlacklist = [],
            HandbookPriceOverride = [],
        };
        typeof(TemplateTable)
            .GetProperty(nameof(TemplateTable.Handbook))!
            .SetValue(
                table,
                new HandbookBase
                {
                    Items = table
                        .Items.Keys.Select(id => new HandbookItem
                        {
                            Id = id,
                            ParentId = new MongoId("000000000000000000000010"),
                            Price = 100,
                        })
                        .ToList(),
                    Categories = [],
                }
            );
        typeof(TemplateTable).GetProperty(nameof(TemplateTable.Prices))!.SetValue(table, new Dictionary<MongoId, double>());
        var cloner = new SPTarkov.Server.Core.Utils.Cloners.FastCloner();
        return new ItemHelper(
            null!,
            table,
            null!,
            new HandbookHelper(null!, table, config, cloner),
            new ItemBaseClassService(null!, table, null!),
            new ItemFilterService(config),
            null!,
            null!,
            cloner
        );
    }
}
