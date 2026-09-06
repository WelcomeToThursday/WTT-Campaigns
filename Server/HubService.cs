using Newtonsoft.Json;
using SeasonalPerks.Shared.Contracts;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Routers;

namespace SeasonalPerks.Server;

[Injectable(InjectionType.Singleton)]
public sealed class HubService
{
    private HubState _state = new();

    public void Initialize(ImageRouter images)
    {
        _state =
            JsonConvert.DeserializeObject<HubState>(File.ReadAllText(Path.Combine(Metadata.DirectoryPath, "data", "hub.json")))
            ?? throw new InvalidDataException("Missing seasonal hub catalogue.");
        var ids = _state
            .Pages.SelectMany(p => p.Rewards)
            .Concat(_state.SeasonalRewards)
            .SelectMany(r => new[] { r.Image, r.BigImage })
            .Concat(_state.Documents.SelectMany(d => new[] { d.Image, d.UnavailableImage }))
            .Append(_state.UniversalImage)
            .Append(_state.UniversalUnavailableImage)
            .Distinct(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            if (id.Length != 24 || id.Any(c => !Uri.IsHexDigit(c)))
            {
                throw new InvalidDataException("Invalid seasonal hub image identifier.");
            }
            var path = Path.Combine(Metadata.DirectoryPath, "hub-images", id + ".png");
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Missing local seasonal hub image", path);
            }
            images.AddRoute("/seasonal-perks/hub-images/" + id, path);
        }
    }

    public string Read()
    {
        return JsonConvert.SerializeObject(_state);
    }
}
