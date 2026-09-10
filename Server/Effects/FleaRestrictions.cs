using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Routers;

namespace WTT.Campaigns.Server.Effects;

[Injectable(InjectionType.Singleton)]
public sealed class FleaRestrictions(EventOutputHolder output)
{
    internal static bool Active(MongoId sessionId)
    {
        return ServerStartup.Seasons.Effects(sessionId.ToString()).Has("flea_market_npc_only");
    }

    internal ItemEventRouterResponse Reject(MongoId sessionId)
    {
        var result = output.GetOutput(sessionId);
        result.Warnings ??= [];
        result.Warnings.Add(
            new Warning { Index = 0, ErrorMessage = "No Flea Market permits trader purchases only for this seasonal character." }
        );
        return result;
    }
}
