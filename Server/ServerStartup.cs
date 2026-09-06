using SeasonalPerks.Server.Hub;
using SeasonalPerks.Server.Profiles;
using SeasonalPerks.Server.Seasons;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Routers;

namespace SeasonalPerks.Server;

[Injectable(InjectionType.Singleton, OnLoadOrder.PostLoad + 800)]
public sealed class ServerStartup(
    SeasonService seasons,
    HubService hub,
    ImageRouter images,
    IEnumerable<IRuntimePatch> patches,
    SeasonRepository repository
) : IOnLoad
{
    internal static SeasonService Seasons = null!;

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        Seasons = seasons;
        seasons.Initialize();
        hub.Initialize(images);
        foreach (var perk in repository.Playable.Values.SelectMany(r => r.Definition.Perks.All).GroupBy(p => p.Id).Select(g => g.First()))
        {
            var file = repository.AssetPath(perk.ImageUrl) ?? "";
            if (!File.Exists(file))
            {
                throw new FileNotFoundException("Missing local seasonal perk icon", file);
            }

            images.AddRoute("/wtt-seasonal/icons/" + perk.Id, file);
            perk.ImageUrl = "/wtt-seasonal/icons/" + perk.Id + ".png";
        }
        foreach (var patch in patches.Where(p => p.GetType().Assembly == typeof(ServerStartup).Assembly))
        {
            patch.Enable();
        }
        return Task.CompletedTask;
    }
}
