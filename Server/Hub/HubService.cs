using Newtonsoft.Json;
using SeasonalPerks.Server.Seasons;
using SeasonalPerks.Shared.Contracts;
using SeasonalPerks.Shared.Seasons;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Routers;

namespace SeasonalPerks.Server.Hub;

[Injectable(InjectionType.Singleton)]
public sealed class HubService(SeasonRepository repository)
{
    private HubState _state = new();

    public void Initialize(ImageRouter images)
    {
        _state = repository.Current.Hub;
        var ids = repository
            .Playable.Values.SelectMany(r => SeasonCompiler.Assets(r.Definition))
            .Where(SeasonValidator.IsId)
            .Distinct(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            if (id.Length != 24 || id.Any(c => !Uri.IsHexDigit(c)))
            {
                throw new InvalidDataException("Invalid seasonal hub image identifier.");
            }
            var path = repository.AssetPath(id) ?? "";
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Missing local seasonal hub image", path);
            }
            images.AddRoute("/wtt-seasonal/hub-images/" + id, path);
        }
    }

    public string Read()
    {
        return JsonConvert.SerializeObject(_state);
    }
}
