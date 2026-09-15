using Newtonsoft.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Utils;
using WTT.Campaigns.Shared.Authoring;

namespace WTT.Campaigns.Server.Editor;

[Injectable]
public sealed class CampaignTestRouter(JsonUtil json, CampaignTestSessions tests) : StaticRouter(json, Routes(tests))
{
    private static List<RouteAction> Routes(CampaignTestSessions tests) =>
        [
            new RouteAction<CampaignTestRouteRequest>(
                CampaignTestRoutes.Create,
                (_, request, id, _, _) => Respond(() => tests.Create(id.ToString(), request))
            ),
            new RouteAction<CampaignTestRouteRequest>(
                CampaignTestRoutes.Status,
                (_, request, id, _, _) => Respond(() => tests.Status(id.ToString(), request))
            ),
            new RouteAction<CampaignTestRouteRequest>(
                CampaignTestRoutes.Reset,
                (_, request, id, _, _) => Respond(() => tests.Reset(id.ToString(), request))
            ),
            new RouteAction<CampaignTestRouteRequest>(
                CampaignTestRoutes.End,
                (_, request, id, _, _) => Respond(() => tests.End(id.ToString(), request))
            ),
        ];

    private static async ValueTask<string> Respond(Func<Task<CampaignTestResponse>> action)
    {
        try
        {
            return JsonConvert.SerializeObject(await action());
        }
        catch (Exception e) when (e is InvalidOperationException or InvalidDataException)
        {
            return JsonConvert.SerializeObject(new CampaignTestResponse { Error = e.Message });
        }
    }
}
