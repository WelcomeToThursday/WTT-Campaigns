using Newtonsoft.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Utils;
using WTT.Campaigns.Server.Profiles;
using WTT.Campaigns.Server.Seasons;
using WTT.Campaigns.Shared.Authoring;

namespace WTT.Campaigns.Server.Editor;

public sealed class SceneCatalogRouteRequest : SceneCatalogRequest, SPTarkov.Server.Core.Models.Utils.IRequestData { }

[Injectable]
public sealed class SceneCatalogRouter(JsonUtil json, TraderOfferCatalogue catalogue, SeasonService seasons)
    : StaticRouter(
        json,
        [
            new RouteAction<SceneCatalogRouteRequest>(
                "/wtt-campaigns/editor/catalogue",
                (_, request, profile, _, _) =>
                {
                    try
                    {
                        if (request.Version != 1)
                            throw new InvalidOperationException("Update both editor components together.");
                        var transportIdentity = profile.ToString();
                        var session = EditorSessionRegistry
                            .Resolve(transportIdentity, request.SessionId, DateTimeOffset.UtcNow)
                            .RequireMap("browsing items");
                        using var lease = seasons.Enter(session.Owner);
                        var current = EditorSessionRegistry.Resolve(transportIdentity, request.SessionId, DateTimeOffset.UtcNow);
                        if (!ReferenceEquals(current.Session, session))
                            throw new InvalidOperationException("Editor map session ended. Return to editor home and reconnect.");
                        session = current.RequireMap("browsing items");
                        session.Contact = DateTimeOffset.UtcNow;
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
