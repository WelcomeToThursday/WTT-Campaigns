using System.Reflection;
using HarmonyLib;
using SeasonalPerks.Server.Effects;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Helpers.Traders;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace SeasonalPerks.Server.Patches.Trading;

[Injectable]
public class TraderAssortPricePatch(TraderPriceEffects prices) : AbstractPatch
{
    private static TraderPriceEffects _prices = null!;

    protected override MethodBase GetTargetMethod()
    {
        _prices = prices;
        return AccessTools.Method(typeof(TraderAssortHelper), nameof(TraderAssortHelper.GetAssort));
    }

    [PatchPostfix]
    private static void Postfix(MongoId sessionId, MongoId traderId, ref TraderAssort __result) =>
        __result = _prices.Assort(
            __result,
            ServerStartup
                .Seasons.Effects(sessionId.ToString())
                .TraderMultiplier(traderId.ToString(), "buy")
        );
}
