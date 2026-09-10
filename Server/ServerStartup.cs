using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Routers;
using WTT.Campaigns.Server.Hub;
using WTT.Campaigns.Server.Patches.Session;
using WTT.Campaigns.Server.Profiles;
using WTT.Campaigns.Server.Seasons;

namespace WTT.Campaigns.Server;

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
        foreach (
            var patch in patches.Where(p =>
                p.GetType().Assembly == typeof(ServerStartup).Assembly && p is not SeasonProfilePathPatch and not SeasonProfileLoadedPatch
            )
        )
        {
            patch.Enable();
        }
        seasons.Initialize();
        hub.Initialize(images);
        foreach (var perk in repository.Playable.Values.SelectMany(r => r.Definition.Perks.All).GroupBy(p => p.Id).Select(g => g.First()))
        {
            var file = repository.AssetPath(perk.ImageUrl) ?? "";
            if (!File.Exists(file))
            {
                throw new FileNotFoundException("Missing local seasonal perk icon", file);
            }

            images.AddRoute("/wtt-campaigns/icons/" + perk.Id, file);
            perk.ImageUrl = "/wtt-campaigns/icons/" + perk.Id + ".png";
        }
        return Task.CompletedTask;
    }
}
