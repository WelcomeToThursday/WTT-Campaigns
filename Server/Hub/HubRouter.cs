using Newtonsoft.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Utils;
using WTT.Campaigns.Shared.Contracts;

namespace WTT.Campaigns.Server.Hub;

[Injectable]
public sealed class HubRouter(JsonUtil json, HubGameplay hub) : StaticRouter(json, Routes(hub))
{
    private static List<RouteAction> Routes(HubGameplay hub)
    {
        return
        [
            new RouteAction<HubRequest>(
                "/wtt-campaigns/hub",
                (_, r, id, _, _) =>
                {
                    try
                    {
                        return ValueTask.FromResult(JsonConvert.SerializeObject(hub.Read(id.ToString(), r.SeasonId, r.CharacterId)));
                    }
                    catch (InvalidOperationException e)
                    {
                        return ValueTask.FromResult(JsonConvert.SerializeObject(new HubState { Error = e.Message }));
                    }
                }
            ),
            new RouteAction<HubRequest>(
                "/wtt-campaigns/hub/claim",
                async (_, r, id, _, _) => await Respond(() => hub.Transact(id.ToString(), r, "claim"))
            ),
            new RouteAction<HubRequest>(
                "/wtt-campaigns/hub/exchange",
                async (_, r, id, _, _) => await Respond(() => hub.Transact(id.ToString(), r, "exchange"))
            ),
            new RouteAction<HubRequest>(
                "/wtt-campaigns/hub/raid-document",
                async (_, r, id, _, _) => await Respond(() => hub.Pickup(id.ToString(), r))
            ),
        ];
    }

    private static async ValueTask<string> Respond(Func<Task<HubResult>> action)
    {
        try
        {
            return JsonConvert.SerializeObject(await action());
        }
        catch (InvalidOperationException e)
        {
            return JsonConvert.SerializeObject(new HubResult { Error = e.Message });
        }
    }
}
