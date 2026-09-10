using SPTarkov.Server.Core.Routers;
using SPTarkov.Server.Core.Services.Image;
using WTT.Campaigns.Server.Hub;
using WTT.Campaigns.Server.Seasons;
using WTT.Campaigns.Shared.Seasons;

namespace WTT.Campaigns.Tests;

internal static class StoryImageRouteChecks
{
    internal static void Run(SeasonRepository store, string directory, Action<bool, string> check)
    {
        // File-only repository and route registry; no HTTP host or game runtime is created.
        var bytes = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg=="
        );
        var draft = store.Create(false);
        var art = store.AddImage(bytes);
        var icon = store.AddImage(bytes);
        draft.Definition.Story = new()
        {
            Chapters =
            [
                new()
                {
                    Id = SeasonRepository.NewId(),
                    Name = "Story-only artwork",
                    Image = art,
                    Icon = icon,
                },
            ],
        };
        // Supply synthetic artwork for the default catalogue inside the temporary repository.
        var folder = Path.Combine(directory, "hub-images");
        Directory.CreateDirectory(folder);
        foreach (var id in SeasonCompiler.Assets(store.Legacy).Concat(SeasonCompiler.Assets(draft.Definition)).Distinct())
        {
            File.WriteAllBytes(Path.Combine(folder, id + ".png"), bytes);
        }
        draft = store.Save(draft);
        var pack = store.Publish(draft, SeasonValidator.Validate(draft.Definition));
        store.Playable[store.Legacy.Id] = new(store.Legacy);
        store.Playable[draft.Definition.Id] = new(store.Pack(pack));
        var routes = new ImageRouterService();
        var router = new ImageRouter(null!, routes, null!);
        new HubService(store).Initialize(router);
        foreach (var id in new[] { art, icon })
        {
            var route = "/wtt-campaigns/hub-images/" + id;
            check(routes.ExistsByKey(route), "Story-only chapter artwork is registered even in a nonactive playable pack");
            check(
                File.ReadAllBytes(routes.GetByKey(route)).SequenceEqual(bytes),
                "Chapter image route resolves the installed pack artwork"
            );
        }
        check(
            routes.ExistsByKey("/wtt-campaigns/hub-images/" + store.Legacy.UniversalImage),
            "Existing hub artwork routes remain registered"
        );
    }
}
