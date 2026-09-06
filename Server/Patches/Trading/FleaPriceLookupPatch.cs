using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using SeasonalPerks.Server.Effects;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Models.Common;

namespace SeasonalPerks.Server.Patches.Trading;

[Injectable]
public class FleaPriceLookupPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(RagfairController), nameof(RagfairController.GetOfferByInternalId));
    }

    [PatchPrefix]
    [UsedImplicitly]
    private static void Prefix(MongoId sessionId, out TraderPriceEffects.SearchScope? __state)
    {
        __state = TraderPriceEffects.Search.Value;
        TraderPriceEffects.Search.Value = new(ServerStartup.Seasons.Effects(sessionId.ToString()));
    }

    [PatchFinalizer]
    [UsedImplicitly]
    private static void Finalizer(TraderPriceEffects.SearchScope? __state)
    {
        TraderPriceEffects.Search.Value = __state;
    }
}
