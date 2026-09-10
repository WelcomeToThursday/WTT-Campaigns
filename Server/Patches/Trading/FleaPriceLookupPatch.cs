using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Models.Common;
using WTT.Campaigns.Server.Effects;
using WTT.Campaigns.Server.Hub;

namespace WTT.Campaigns.Server.Patches.Trading;

[Injectable]
public class FleaPriceLookupPatch(HubGameplay hub) : AbstractPatch
{
    private static HubGameplay _hub = null!;

    protected override MethodBase GetTargetMethod()
    {
        _hub = hub;
        return AccessTools.Method(typeof(RagfairController), nameof(RagfairController.GetOfferByInternalId));
    }

    [PatchPrefix]
    [UsedImplicitly]
    private static void Prefix(MongoId sessionId, out TraderPriceEffects.SearchScope? __state)
    {
        __state = TraderPriceEffects.Search.Value;
        TraderPriceEffects.Search.Value = new(
            ServerStartup.Seasons.Effects(sessionId.ToString()),
            offer => _hub.FleaOfferAllowed(sessionId.ToString(), offer)
        );
    }

    [PatchFinalizer]
    [UsedImplicitly]
    private static void Finalizer(TraderPriceEffects.SearchScope? __state)
    {
        TraderPriceEffects.Search.Value = __state;
    }
}
