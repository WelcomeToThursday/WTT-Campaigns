using Newtonsoft.Json;
using SeasonalPerks.Server.Profiles;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Utils;

namespace SeasonalPerks.Server.Routing;

[Injectable]
public sealed class SeasonRouter(JsonUtil json, SeasonService seasons) : StaticRouter(json, Routes(json, seasons))
{
    private static List<RouteAction> Routes(JsonUtil json, SeasonService s)
    {
        return
        [
            new RouteAction<SeasonRequest>(
                "/seasonal-perks/snapshot",
                async (_, _, id, _, _) => await Respond(json, s, id.ToString(), root => Task.FromResult(s.GetSnapshot(root)))
            ),
            new RouteAction<SeasonRequest>(
                "/seasonal-perks/create",
                async (_, r, id, _, _) => await Respond(json, s, id.ToString(), root => s.Create(root, r.ToMutation()))
            ),
            new RouteAction<SeasonRequest>(
                "/seasonal-perks/edit",
                async (_, r, id, _, _) => await Respond(json, s, id.ToString(), root => s.Edit(root, r.ToMutation()))
            ),
            new RouteAction<SeasonRequest>(
                "/seasonal-perks/switch",
                async (_, r, id, _, _) => await Respond(json, s, id.ToString(), root => s.Switch(root, r.Mode))
            ),
        ];
    }

    private static async ValueTask<string> Respond(
        JsonUtil json,
        SeasonService seasons,
        string root,
        Func<string, Task<ServerSnapshot>> action
    )
    {
        try
        {
            root = seasons.ResolveRoot(root);
            using var lease = seasons.Enter(root);
            return JsonConvert.SerializeObject(await action(root), new CharacterVisualConverter(json));
        }
        catch (InvalidOperationException e)
        {
            return JsonConvert.SerializeObject(new ServerSnapshot { Error = e.Message });
        }
    }
}
