using Newtonsoft.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils;
using WTT.Campaigns.Server.Profiles;
using WTT.Campaigns.Server.Web.Authoring;
using WTT.Campaigns.Shared.Authoring;

namespace WTT.Campaigns.Server.Routing;

public sealed class AuthoringRouteRequest : AuthoringRequest, IRequestData;

[Injectable]
public sealed class AuthoringRouter(JsonUtil json, SeasonService seasons, RaidAuthoringService authoring)
    : StaticRouter(json, Routes(seasons, authoring))
{
    private static List<RouteAction> Routes(SeasonService seasons, RaidAuthoringService authoring)
    {
        return new[] { "poll", "submit" }
            .Select(operation =>
                (RouteAction)
                    new RouteAction<AuthoringRouteRequest>(
                        "/wtt-campaigns/authoring/" + operation,
                        (_, r, id, _, _) =>
                        {
                            try
                            {
                                var root = seasons.ResolveRoot(id.ToString());
                                using var lease = seasons.Enter(root);
                                var character = seasons.EffectiveId(root);
                                var result =
                                    operation == "poll" ? authoring.Poll(root, character, r) : authoring.Submit(root, character, r);
                                return ValueTask.FromResult(JsonConvert.SerializeObject(result));
                            }
                            catch (Exception e) when (e is InvalidOperationException or ArgumentException or IOException)
                            {
                                return ValueTask.FromResult(JsonConvert.SerializeObject(new AuthoringResponse { Error = e.Message }));
                            }
                        }
                    )
            )
            .ToList();
    }
}
