using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Utils;

namespace SeasonalPerks.Server;

[Injectable]
public sealed class HubRouter(JsonUtil json, HubService hub)
    : StaticRouter(json, [new RouteAction<SeasonRequest>("/seasonal-perks/hub", (_, _, _, _, _) => ValueTask.FromResult(hub.Read()))]);
