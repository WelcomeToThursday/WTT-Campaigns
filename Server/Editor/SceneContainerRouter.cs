using Newtonsoft.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Utils;
using WTT.Campaigns.Server.Profiles;
using WTT.Campaigns.Server.Seasons;
using WTT.Campaigns.Shared.Authoring;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Server.Editor;

public sealed class SceneContainerRouteRequest : SceneContainerRequest, SPTarkov.Server.Core.Models.Utils.IRequestData { }

[Injectable(InjectionType.Singleton)]
public sealed class SceneContainerSessions(SeasonRepository repository, SeasonService seasons, SceneContainerLoot loot)
{
    private readonly Dictionary<string, (string Profile, SceneContainerRunCache Cache)> _runs = new();

    public SceneContainerResponse Read(string identity, SceneContainerRequest request)
    {
        var session = EditorSessionRegistry.Resolve(identity, request.SessionId, DateTimeOffset.UtcNow).RequireMap("preparing containers");
        using var lease = seasons.Enter(session.Owner);
        var current = EditorSessionRegistry.Resolve(identity, request.SessionId, DateTimeOffset.UtcNow);
        if (!current.Accepted || current.Session != session)
            throw new InvalidOperationException("The editor session ended.");
        session.Contact = DateTimeOffset.UtcNow;
        if (request.RunId.Length == 0)
            return new SceneContainerResponse { Templates = loot.Templates(session.Location) };
        if (
            (!Guid.TryParseExact(request.RunId, "N", out _) && !WTT.Campaigns.Shared.Seasons.SeasonValidator.IsId(request.RunId))
            || !WTT.Campaigns.Shared.Seasons.SeasonValidator.IsId(request.LayoutId)
        )
            throw new InvalidOperationException("The container request does not match the editor layout.");
        var layout = repository
            .Load(session.Draft)
            .Definition.MapLayouts.Single(l => l.Id == request.LayoutId && l.Location == session.Location);
        var errors = MapLayoutRules.Errors(layout);
        if (errors.Count > 0)
            throw new InvalidOperationException(errors[0]);
        lock (_runs)
        {
            foreach (var stale in _runs.Where(p => EditorSessionRegistry.Find(p.Value.Profile)?.Id != p.Key).Select(p => p.Key).ToArray())
                _runs.Remove(stale);
            if (!_runs.TryGetValue(session.Id, out var state))
                _runs.Add(session.Id, state = (session.Profile, new SceneContainerRunCache()));
            return state.Cache.Get(request.RunId, layout.Id, () => new SceneContainerResponse { Contents = loot.Create(layout) });
        }
    }
}

[Injectable]
public sealed class SceneContainerRouter(JsonUtil json, SceneContainerSessions sessions)
    : StaticRouter(
        json,
        [
            new RouteAction<SceneContainerRouteRequest>(
                "/wtt-campaigns/editor/containers",
                (_, request, identity, _, _) =>
                {
                    try
                    {
                        return new ValueTask<string>(JsonConvert.SerializeObject(sessions.Read(identity.ToString(), request)));
                    }
                    catch (Exception e)
                    {
                        return new ValueTask<string>(JsonConvert.SerializeObject(new SceneContainerResponse { Error = e.Message }));
                    }
                }
            ),
        ]
    );
