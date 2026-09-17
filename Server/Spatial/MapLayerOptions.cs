using Newtonsoft.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services.Modding;
using SPTarkov.Server.Core.Services.Profile;
using SPTarkov.Server.Core.Utils;
using WTT.Campaigns.Server.Profiles;
using WTT.Campaigns.Server.Seasons;
using WTT.Campaigns.Shared.Seasons;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Server.Spatial;

[Injectable(InjectionType.Singleton)]
public sealed class MapLayerOptions(SeasonService seasons, SeasonRepository repository, ProfileDataService data)
{
    private const string StorageKey = "wttCampaignsMapLayers";

    internal MapLayerPreferences ReadSaved(string character) =>
        data.GetProfileDataAsync<MapLayerPreferences>(new MongoId(character), StorageKey).GetAwaiter().GetResult() ?? new();

    public async Task<MapLayerOptionsResponse> Read(string identity, MapLayerOptionsRequest request, bool change)
    {
        var root = seasons.ResolveRoot(identity);
        using var lease = seasons.Enter(root);
        if (identity != root || identity != request.CharacterId || seasons.EffectiveId(root) != identity || seasons.IsSeasonal(identity))
            throw new InvalidOperationException("Open your regular character to choose map layers.");
        seasons.EnsureNotInRaid(identity);
        var saved = ReadSaved(identity);
        var published = repository.OrdinaryPlayable().Select(p => p.Definition).ToArray();
        if (change)
        {
            if (saved.Revision != request.Revision)
                throw new InvalidOperationException("Map layer selections changed. Reopen the screen to refresh them.");
            var match = published
                .SelectMany(p => p.MapLayouts.Select(l => (Campaign: p.Id, Layout: l)))
                .SingleOrDefault(p => MapLayerRules.Key(p.Campaign, p.Layout.Id) == request.Key);
            if (match.Layout == null)
                throw new InvalidOperationException("This published layer is no longer available.");
            var next = SeasonCompiler.Copy(saved);
            next.Overrides[request.Key] = request.Enabled;
            // Disabling must remain possible even when other enabled layers conflict.
            if (request.Enabled)
                MapLayerRules.ForCharacter(published, "", match.Layout.Location, next.Overrides);
            next.Revision++;
            await data.SaveProfileDataAsync(new MongoId(identity), StorageKey, next);
            saved = next;
        }
        return new MapLayerOptionsResponse
        {
            CharacterId = identity,
            Revision = saved.Revision,
            Layers = published
                .SelectMany(p =>
                    p.MapLayouts.Select(l => new MapLayerOption
                    {
                        Key = MapLayerRules.Key(p.Id, l.Id),
                        Name = l.Name,
                        Campaign = p.Name,
                        Location = l.Location,
                        Enabled = MapLayerRules.Enabled(p.Id, l, saved.Overrides),
                    })
                )
                .OrderBy(l => l.Location, StringComparer.Ordinal)
                .ThenBy(l => l.Campaign)
                .ThenBy(l => l.Name)
                .ToList(),
        };
    }
}

public sealed class MapLayerOptionsRouteRequest : MapLayerOptionsRequest, IRequestData { }

[Injectable]
public sealed class MapLayerOptionsRouter(JsonUtil json, MapLayerOptions options)
    : StaticRouter(
        json,
        [
            new RouteAction<MapLayerOptionsRouteRequest>(
                "/wtt-campaigns/map-layers/options",
                (_, request, id, _, _) => Respond(options, id.ToString(), request, false)
            ),
            new RouteAction<MapLayerOptionsRouteRequest>(
                "/wtt-campaigns/map-layers/select",
                (_, request, id, _, _) => Respond(options, id.ToString(), request, true)
            ),
        ]
    )
{
    private static async ValueTask<string> Respond(MapLayerOptions options, string id, MapLayerOptionsRequest request, bool change)
    {
        try
        {
            return JsonConvert.SerializeObject(await options.Read(id, request, change));
        }
        catch (InvalidOperationException exception)
        {
            return JsonConvert.SerializeObject(new MapLayerOptionsResponse { Error = exception.Message });
        }
    }
}
