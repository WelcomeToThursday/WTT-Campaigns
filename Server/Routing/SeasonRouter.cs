using Newtonsoft.Json;
using SeasonalPerks.Server.Profiles;
using SeasonalPerks.Server.Seasons;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Utils;

namespace SeasonalPerks.Server.Routing;

[Injectable]
public sealed class SeasonRouter(JsonUtil json, SeasonService seasons, SeasonRepository repository)
    : StaticRouter(json, Routes(json, seasons, repository))
{
    private static List<RouteAction> Routes(JsonUtil json, SeasonService s, SeasonRepository repository)
    {
        return
        [
            new RouteAction<SeasonRequest>(
                "/wtt-seasonal/snapshot",
                async (_, r, id, _, _) => await Respond(json, s, id.ToString(), r, repository, root => Task.FromResult(s.GetSnapshot(root)))
            ),
            new RouteAction<SeasonRequest>(
                "/wtt-seasonal/create",
                async (_, r, id, _, _) => await Respond(json, s, id.ToString(), r, repository, root => s.Create(root, r.ToMutation()))
            ),
            new RouteAction<SeasonRequest>(
                "/wtt-seasonal/edit",
                async (_, r, id, _, _) => await Respond(json, s, id.ToString(), r, repository, root => s.Edit(root, r.ToMutation()))
            ),
            new RouteAction<SeasonRequest>(
                "/wtt-seasonal/switch",
                async (_, r, id, _, _) => await Respond(json, s, id.ToString(), r, repository, root => s.Switch(root, r.Mode))
            ),
        ];
    }

    private static async ValueTask<string> Respond(
        JsonUtil json,
        SeasonService seasons,
        string root,
        SeasonRequest request,
        SeasonRepository repository,
        Func<string, Task<ServerSnapshot>> action
    )
    {
        try
        {
            if (!repository.Current.Definition.Legacy && request.ProtocolVersion != 2)
            {
                throw new InvalidOperationException("Update the Seasonal client and server together (creator protocol 2 required).");
            }

            if (request.SeasonId.Length > 0 && request.SeasonId != repository.Current.Definition.Id)
            {
                throw new InvalidOperationException("The active season changed. Reconnect to the server.");
            }

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
