using System.Reflection;
using HarmonyLib;
using SeasonalPerks.Server.Effects;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Models.Eft.Ragfair;
using SPTarkov.Server.Core.Services.Ragfair;

namespace SeasonalPerks.Server.Patches.Trading;

[Injectable]
public class FleaOfferTypePricesPatch(TraderPriceEffects prices) : AbstractPatch
{
    private static TraderPriceEffects _prices = null!;

    protected override MethodBase GetTargetMethod()
    {
        _prices = prices;
        return AccessTools.Method(
            typeof(RagfairOfferService),
            nameof(RagfairOfferService.GetOffersOfType)
        );
    }

    [PatchPostfix]
    private static void Postfix(ref IEnumerable<RagfairOffer>? __result)
    {
        if (TraderPriceEffects.Search.Value != null && __result != null)
            __result = __result.Where(TraderPriceEffects.Visible).Select(_prices.Offer).ToList();
    }
}
