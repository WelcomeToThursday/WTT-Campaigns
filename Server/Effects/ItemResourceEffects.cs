using SeasonalPerks.Server.Profiles;
using SeasonalPerks.Shared.Effects;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace SeasonalPerks.Server.Effects;

internal static class ItemResourceEffects
{
    internal static float Multiplier(PmcData pmc, Item item, ItemHelper items)
    {
        var effects = new RuntimeEffects(ServerStartup.Seasons.Catalogue, SeasonService.State(pmc).SeasonalPerks);
        var parents = new List<string>();
        var template = items.GetItem(item.Template).Value;
        var seen = new HashSet<MongoId>();
        while (template?.Parent is { } parent && seen.Add(parent))
        {
            parents.Add(parent.ToString());
            template = items.GetItem(parent).Value;
        }
        return effects.ItemResourceMultiplier(item.Template.ToString(), parents);
    }
}
