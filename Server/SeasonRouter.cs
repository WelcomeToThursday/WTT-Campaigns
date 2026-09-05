using Newtonsoft.Json;
using SeasonalPerks.Shared;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils;

namespace SeasonalPerks.Server;

[Injectable]
public sealed class SeasonRouter(JsonUtil json, SeasonService seasons)
    : StaticRouter(json, Routes(seasons))
{
    private static List<RouteAction> Routes(SeasonService s) =>
        [
            new RouteAction<SeasonRequest>(
                "/seasonal-perks/snapshot",
                async (_, _, id, _, _) =>
                    await Respond(s, id.ToString(), root => Task.FromResult(s.GetSnapshot(root)))
            ),
            new RouteAction<SeasonRequest>(
                "/seasonal-perks/create",
                async (_, r, id, _, _) =>
                    await Respond(s, id.ToString(), root => s.Create(root, r.ToMutation()))
            ),
            new RouteAction<SeasonRequest>(
                "/seasonal-perks/edit",
                async (_, r, id, _, _) =>
                    await Respond(s, id.ToString(), root => s.Edit(root, r.ToMutation()))
            ),
            new RouteAction<SeasonRequest>(
                "/seasonal-perks/switch",
                async (_, r, id, _, _) =>
                    await Respond(s, id.ToString(), root => s.Switch(root, r.Mode))
            ),
        ];

    private static async ValueTask<string> Respond(
        SeasonService seasons,
        string root,
        Func<string, Task<Snapshot>> action
    )
    {
        try
        {
            root = seasons.ResolveRoot(root);
            using var lease = seasons.Enter(root);
            return JsonConvert.SerializeObject(await action(root));
        }
        catch (InvalidOperationException e)
        {
            return JsonConvert.SerializeObject(new Snapshot { Error = e.Message });
        }
    }
}
