using Microsoft.AspNetCore.Http;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Routers;

namespace SeasonalPerks.Server;

[Injectable(InjectionType.Singleton, OnLoadOrder.Preload)]
public sealed class ServerStartup(
    SeasonService seasons,
    ImageRouter images,
    IEnumerable<IRuntimePatch> patches
) : IOnLoad
{
    internal static SeasonService Seasons = null!;

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        Seasons = seasons;
        seasons.Initialize();
        foreach (var perk in seasons.Catalogue.All)
        {
            var file = Path.Combine(
                Metadata.DirectoryPath,
                "icons",
                Path.GetFileName(perk.ImageUrl)
            );
            if (!File.Exists(file))
            {
                throw new FileNotFoundException("Missing local seasonal perk icon", file);
            }

            images.AddRoute("/seasonal-perks/icons/" + perk.Id, file);
            perk.ImageUrl = "/seasonal-perks/icons/" + perk.Id + ".png";
        }
        foreach (
            var patch in patches.Where(p => p.GetType().Assembly == typeof(ServerStartup).Assembly)
        )
        {
            patch.Enable();
        }
        return Task.CompletedTask;
    }
}
