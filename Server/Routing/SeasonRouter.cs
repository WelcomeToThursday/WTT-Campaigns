using Newtonsoft.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Utils;
using WTT.Campaigns.Server.Profiles;
using WTT.Campaigns.Server.Seasons;

namespace WTT.Campaigns.Server.Routing;

[Injectable]
public sealed class SeasonRouter(
    JsonUtil json,
    SeasonService seasons,
    SeasonRepository repository,
    WTT.Campaigns.Server.Story.StoryService story
) : StaticRouter(json, Routes(json, seasons, repository, story))
{
    private static List<RouteAction> Routes(
        JsonUtil json,
        SeasonService s,
        SeasonRepository repository,
        WTT.Campaigns.Server.Story.StoryService story
    )
    {
        return
        [
            new RouteAction<SeasonRequest>(
                "/wtt-campaigns/raid-abort",
                async (_, r, id, _, _) =>
                    await Respond(
                        json,
                        s,
                        id.ToString(),
                        r,
                        repository,
                        async root =>
                        {
                            await s.AbortRaid(root, r.CharacterId, r.OperationId, story.ResetSessionUnderLease);
                            return new ServerSnapshot();
                        }
                    )
            ),
            new RouteAction<SeasonRequest>(
                "/wtt-campaigns/snapshot",
                async (_, r, id, _, _) =>
                    await Respond(
                        json,
                        s,
                        id.ToString(),
                        r,
                        repository,
                        root => Task.FromResult(s.GetSnapshot(root, r.SeasonId, r.CharacterId))
                    )
            ),
            new RouteAction<SeasonRequest>(
                "/wtt-campaigns/create",
                async (_, r, id, _, _) => await Respond(json, s, id.ToString(), r, repository, root => s.Create(root, r.ToMutation()))
            ),
            new RouteAction<SeasonRequest>(
                "/wtt-campaigns/edit",
                async (_, r, id, _, _) => await Respond(json, s, id.ToString(), r, repository, root => s.Edit(root, r.ToMutation()))
            ),
            new RouteAction<SeasonRequest>(
                "/wtt-campaigns/switch",
                async (_, r, id, _, _) =>
                    await Respond(json, s, id.ToString(), r, repository, root => s.Switch(root, r.Mode, r.CharacterId))
            ),
            new RouteAction<SeasonRequest>(
                "/wtt-campaigns/delete",
                async (_, r, id, _, _) => await Respond(json, s, id.ToString(), r, repository, root => s.Delete(root, r.ToMutation()))
            ),
            new RouteAction<SeasonRequest>(
                "/wtt-campaigns/wipe",
                async (_, r, id, _, _) => await Respond(json, s, id.ToString(), r, repository, root => s.Wipe(root, r.ToMutation()))
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
                throw new InvalidOperationException("Update the WTT-Campaigns client and server together (creator protocol 2 required).");
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
