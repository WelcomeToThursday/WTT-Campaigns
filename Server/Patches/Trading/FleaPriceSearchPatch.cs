using System.Reflection;
using HarmonyLib;
using SeasonalPerks.Server.Effects;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Models.Common;

namespace SeasonalPerks.Server.Patches.Trading;

[Injectable]
public class FleaPriceSearchPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod() =>
        AccessTools.Method(typeof(RagfairController), nameof(RagfairController.GetOffers));

    [PatchPrefix]
    private static void Prefix(MongoId sessionID, out TraderPriceEffects.SearchScope? __state)
    {
        __state = TraderPriceEffects.Search.Value;
        TraderPriceEffects.Search.Value = new(ServerStartup.Seasons.Effects(sessionID.ToString()));
    }

    [PatchFinalizer]
    private static void Finalizer(TraderPriceEffects.SearchScope? __state) =>
        TraderPriceEffects.Search.Value = __state;
}
