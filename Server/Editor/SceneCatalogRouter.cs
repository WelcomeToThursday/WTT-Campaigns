using Newtonsoft.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Utils;
using WTT.Campaigns.Server.Seasons;
using WTT.Campaigns.Shared.Authoring;

namespace WTT.Campaigns.Server.Editor;

public sealed class SceneCatalogRouteRequest : SceneCatalogRequest, SPTarkov.Server.Core.Models.Utils.IRequestData { }

[Injectable]
public sealed class SceneCatalogRouter(JsonUtil json, TraderOfferCatalogue catalogue)
    : StaticRouter(
        json,
        [
            new RouteAction<SceneCatalogRouteRequest>(
                "/wtt-campaigns/editor/catalogue",
                (_, request, profile, _, _) =>
                {
                    try
                    {
                        var session = EditorSessionRegistry.Find(profile.ToString());
                        if (
                            request.Version != 1
                            || session == null
                            || !session.Accepts(session.Owner, request.SessionId, DateTimeOffset.UtcNow)
                            || session.Location.Length == 0
                        )
                            throw new InvalidOperationException("Connect an active editor map before browsing items.");
                        return new ValueTask<string>(JsonConvert.SerializeObject(catalogue.SceneCatalog(request)));
                    }
                    catch (Exception e)
                    {
                        return new ValueTask<string>(JsonConvert.SerializeObject(new SceneCatalogResponse { Error = e.Message }));
                    }
                }
            ),
        ]
    );
