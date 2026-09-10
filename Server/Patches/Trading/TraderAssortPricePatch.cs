using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Helpers.Traders;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using WTT.Campaigns.Server.Effects;
using WTT.Campaigns.Server.Hub;

namespace WTT.Campaigns.Server.Patches.Trading;

[Injectable]
public class TraderAssortPricePatch(TraderPriceEffects prices, HubGameplay hub) : AbstractPatch
{
    private static TraderPriceEffects _prices = null!;
    private static HubGameplay _hub = null!;

    protected override MethodBase GetTargetMethod()
    {
        _prices = prices;
        _hub = hub;
        return AccessTools.Method(typeof(TraderAssortHelper), nameof(TraderAssortHelper.GetAssort));
    }

    [PatchPostfix]
    [UsedImplicitly]
    private static void Postfix(MongoId sessionId, MongoId traderId, ref TraderAssort __result)
    {
        __result = _prices.Assort(
            _hub.FilterOffers(sessionId.ToString(), traderId.ToString(), __result),
            ServerStartup.Seasons.Effects(sessionId.ToString()).TraderMultiplier(traderId.ToString(), "buy")
        );
    }

    [PatchPrefix]
    [UsedImplicitly]
    private static void Prefix(MongoId traderId)
    {
        _hub.RestoreFallbackOffers(traderId.ToString());
    }
}
