using Newtonsoft.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Utils;
using WTT.Campaigns.Shared.Story;

namespace WTT.Campaigns.Server.Story;

[Injectable]
public sealed class StoryRouter(JsonUtil json, StoryService story) : StaticRouter(json, Routes(story))
{
    private static List<RouteAction> Routes(StoryService story)
    {
        var routes = new List<RouteAction>
        {
            new RouteAction<StoryRouteRequest>(
                "/wtt-campaigns/story/prepare",
                (_, r, id, _, _) => Respond(() => story.Transact(id.ToString(), r, r.Operation, true))
            ),
            new RouteAction<StoryRouteRequest>(
                "/wtt-campaigns/story",
                (_, r, id, _, _) => Respond(() => Task.FromResult(story.Read(id.ToString(), r)))
            ),
        };
        foreach (var operation in new[] { "start", "select", "close", "read", "reconcile", "raid" })
        {
            var name = operation;
            routes.Add(
                new RouteAction<StoryRouteRequest>(
                    "/wtt-campaigns/story/" + name,
                    (_, r, id, _, _) => Respond(() => story.Transact(id.ToString(), r, name))
                )
            );
        }
        return routes;
    }

    private static async ValueTask<string> Respond(Func<Task<StoryResponse>> action)
    {
        try
        {
            return JsonConvert.SerializeObject(await action());
        }
        catch (InvalidOperationException exception)
        {
            return JsonConvert.SerializeObject(new StoryResponse { Error = exception.Message });
        }
    }
}
