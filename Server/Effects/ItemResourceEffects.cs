using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using WTT.Campaigns.Server.Profiles;
using WTT.Campaigns.Shared.Effects;

namespace WTT.Campaigns.Server.Effects;

internal static class ItemResourceEffects
{
    internal static float Multiplier(PmcData pmc, Item item, ItemHelper items)
    {
        var effects = ServerStartup.Seasons.Effects(pmc);
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
