using Newtonsoft.Json;
using SeasonalPerks.Server.Profiles;
using SeasonalPerks.Server.Web.Authoring;
using SeasonalPerks.Shared.Authoring;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils;

namespace SeasonalPerks.Server.Routing;

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
                        "/wtt-seasonal/authoring/" + operation,
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
