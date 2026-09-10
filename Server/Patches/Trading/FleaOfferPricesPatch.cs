using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Models.Eft.Ragfair;
using SPTarkov.Server.Core.Services.Ragfair;
using WTT.Campaigns.Server.Effects;

namespace WTT.Campaigns.Server.Patches.Trading;

[Injectable]
public class FleaOfferPricesPatch(TraderPriceEffects prices) : AbstractPatch
{
    private static TraderPriceEffects _prices = null!;

    protected override MethodBase GetTargetMethod()
    {
        _prices = prices;
        return AccessTools.Method(typeof(RagfairOfferService), nameof(RagfairOfferService.GetOffers));
    }

    [PatchPostfix]
    [UsedImplicitly]
    private static void Postfix(ref List<RagfairOffer> __result)
    {
        if (TraderPriceEffects.Search.Value != null)
        {
            __result = __result.Where(TraderPriceEffects.Visible).Select(_prices.Offer).ToList();
        }
    }
}
