using System.Collections.Concurrent;
using Newtonsoft.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils;
using WTT.Campaigns.Server.Editor;
using WTT.Campaigns.Server.Profiles;
using WTT.Campaigns.Server.Seasons;
using WTT.Campaigns.Shared.Seasons;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Server.Spatial;

[Injectable(InjectionType.Singleton)]
public sealed class MapLayerService(
    SeasonService seasons,
    SeasonRepository repository,
    SceneContainerLoot containers,
    MapLayerOptions options
)
{
    private readonly ConcurrentDictionary<string, MapLayerResponse> _raids = new(StringComparer.Ordinal);

    private MapLayout? Compose(string character, string location)
    {
        var seasonId = seasons.IsSeasonal(character) ? seasons.CharacterSeasonId(character) : "";
        var published =
            seasonId.Length > 0
                ? new[] { repository.Runtime(seasonId).Definition }
                : repository.OrdinaryPlayable().Select(p => p.Definition);
        return MapLayerRules.ForCharacter(
            published,
            seasonId,
            location,
            seasonId.Length == 0 ? options.ReadSaved(character).Overrides : null
        );
    }

    public void Validate(string character, string location) => Compose(character, location);

    // Capture once at native raid start: draft changes and retries never reroll live content.
    public void Prepare(string character, string location, string raidId, bool mission)
    {
        _raids.TryRemove(character, out _);
        if (mission)
            return;
        var root = seasons.ResolveRoot(character);
        using var lease = seasons.Enter(root);
        if (seasons.EffectiveId(root) != character)
            throw new InvalidOperationException("The map layer character is no longer active.");
        var seasonId = seasons.IsSeasonal(character) ? seasons.CharacterSeasonId(character) : "";
        var layout = Compose(character, location);
        _raids[character] = new MapLayerResponse
        {
            CharacterId = character,
            SeasonId = seasonId,
            Location = location,
            RaidId = raidId,
            Layout = layout == null ? null : SeasonCompiler.Copy(layout),
            ContainerLoot = layout == null ? new() : containers.Create(layout),
        };
    }

    public MapLayerResponse Read(string identity, MapLayerRequest request)
    {
        var root = seasons.ResolveRoot(identity);
        using var lease = seasons.Enter(root);
        var character = seasons.EffectiveId(root);
        if (identity != character || request.CharacterId != character)
            throw new InvalidOperationException("Map layers require the authenticated active character.");
        if (
            !_raids.TryGetValue(character, out var response)
            || !seasons.HasActiveMapLayerRaid(character, response.RaidId)
            || response.SeasonId != request.SeasonId
            || response.Location != request.Location
        )
            throw new InvalidOperationException("Map layers do not match the active ordinary raid.");
        return response;
    }

    public void End(string character) => _raids.TryRemove(character, out _);
}

public sealed class MapLayerRouteRequest : MapLayerRequest, IRequestData { }

[Injectable]
public sealed class MapLayerRouter(JsonUtil json, MapLayerService layers)
    : StaticRouter(
        json,
        [
            new RouteAction<MapLayerRouteRequest>(
                "/wtt-campaigns/map-layers",
                (_, request, id, _, _) =>
                {
                    try
                    {
                        return new ValueTask<string>(JsonConvert.SerializeObject(layers.Read(id.ToString(), request)));
                    }
                    catch (InvalidOperationException exception)
                    {
                        return new ValueTask<string>(JsonConvert.SerializeObject(new MapLayerResponse { Error = exception.Message }));
                    }
                }
            ),
        ]
    );
