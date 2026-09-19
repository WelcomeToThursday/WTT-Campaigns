using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using WTT.Campaigns.Server.Seasons;
using WTT.Campaigns.Server.Web.Components;
using WTT.Campaigns.Shared.Seasons;
using WTT.Campaigns.Shared.Spatial;

internal static class MapLayoutUiChecks
{
    internal static async Task Run(IServiceProvider services, Action<bool, string> check)
    {
        var map = new MapLayout
        {
            Id = SeasonRepository.NewId(),
            Name = "Building route",
            Location = "woods",
            Checkpoints = new()
            {
                new MapVolume
                {
                    Id = SeasonRepository.NewId(),
                    Name = "Stairs",
                    Location = "woods",
                    Scene = "woods_main",
                },
            },
        };
        var asset = new MapTarget
        {
            Kind = "AssetContainer",
            Bundle = "assets/crate.bundle",
            Asset = "assets/crate.prefab",
            Template = "578f8778245977358849a9b5",
        };
        asset.Fingerprint = SceneAssetRules.Identity(asset.Bundle, asset.Asset);
        map.Objects.Add(
            new MapObjectEdit
            {
                Id = SeasonRepository.NewId(),
                Name = "Native crate",
                Location = map.Location,
                Scene = "woods_main",
                Target = asset,
                Operation = "Copy",
            }
        );
        var season = new SeasonDefinition
        {
            FormatVersion = 8,
            MapLayouts = new() { map },
        };
        var host = new MapHost(season);
        await using var renderer = new EditorRenderer(services);
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.Mount(host);
            var component = renderer.Components<MapLayoutWorkspace>().Single();
            check(
                renderer.Text(component.Id).Contains("Building route") && !renderer.Text(component.Id).Contains("Walkthrough requires"),
                "Ordinary levels do not require mission routes even when legacy checkpoints are preserved"
            );
            check(
                renderer.Text(component.Id).Contains("assets/crate.bundle")
                    && renderer.Text(component.Id).Contains("Native random-loot container"),
                "Creator displays independent asset references and container behavior"
            );
            check(renderer.Text(component.Id).Contains("Apply in normal raids"), "Creator exposes the ordinary-raid layer default");
            await renderer.DispatchEventAsync(
                renderer.Event(component.Id, "input", "", "onchange", "checkbox"),
                null,
                new ChangeEventArgs { Value = true }
            );
            check(map.ApplyInNormalRaids && host.Changes == 1, "Enabling a normal-raid layer persists through the real Creator control");
            await renderer.DispatchEventAsync(
                renderer.Event(component.Id, "input", "", "onchange", "checkbox"),
                null,
                new ChangeEventArgs { Value = false }
            );
            check(!map.ApplyInNormalRaids && host.Changes == 2, "Disabling a layer reaches the draft change handler");
            await renderer.DispatchEventAsync(
                renderer.Event(component.Id, "input", "", "onchange"),
                null,
                new ChangeEventArgs { Value = "Hallway escape" }
            );
            check(map.Name == "Hallway escape" && host.Changes == 3, "Layout rename reaches the draft change handler");
            await renderer.DispatchEventAsync(
                renderer.Event(component.Id, "button", "Duplicate layout", "onclick"),
                null,
                new MouseEventArgs()
            );
            check(season.MapLayouts.Count == 2 && host.Changes == 4, "Creator duplicates layouts through the production component");
            check(
                !MapLayoutRules.OwnedIds(map).Intersect(MapLayoutRules.OwnedIds(season.MapLayouts[1])).Any(),
                "Creator duplication gives route records independent identities"
            );
            check(
                season.MapLayouts[1].Objects[0].Target.Asset == asset.Asset && season.FormatVersion == 8,
                "Creator duplication preserves asset references and format 8"
            );
            season.MapLayouts[1].Checkpoints[0].Position.X = 9;
            check(map.Checkpoints[0].Position.X == 0, "Duplicated layout geometry is independent");
            await renderer.DispatchEventAsync(
                renderer.Event(component.Id, "button", "Delete layout", "onclick"),
                null,
                new MouseEventArgs()
            );
            check(
                season.MapLayouts.Count == 1 && season.MapLayouts[0].Name == "Hallway escape copy" && host.Changes == 5,
                "Deleting a layout keeps the independent copy and signals persistence"
            );
        });

        var zone = new SeasonZone
        {
            Id = SeasonRepository.NewId(),
            Name = "Shared doorway",
            Location = "woods",
            Scene = "woods_main",
            LayoutId = season.MapLayouts[0].Id,
        };
        season.Zones.Add(zone);
        var spatialHost = new SpatialHost(season);
        await using var spatialRenderer = new EditorRenderer(services);
        await spatialRenderer.Dispatcher.InvokeAsync(async () =>
        {
            await spatialRenderer.Mount(spatialHost);
            var component = spatialRenderer.Components<SpatialWorkspace>().Single();
            check(spatialRenderer.Text(component.Id).Contains("Hallway escape copy"), "Zone list shows its layout scope");
            await spatialRenderer.DispatchEventAsync(
                spatialRenderer.Event(component.Id, "select", "", "onchange"),
                null,
                new ChangeEventArgs { Value = "" }
            );
            check(
                zone.LayoutId.Length == 0 && spatialHost.Changes == 1 && spatialRenderer.Text(component.Id).Contains("Shared"),
                "Changing zone scope updates the list summary immediately"
            );
        });
    }

    private sealed class MapHost(SeasonDefinition season) : ComponentBase
    {
        internal int Changes;

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<MapLayoutWorkspace>(0);
            builder.AddAttribute(1, "Season", season);
            builder.AddAttribute(2, "Changed", EventCallback.Factory.Create(this, () => Changes++));
            builder.CloseComponent();
        }
    }

    private sealed class SpatialHost(SeasonDefinition season) : ComponentBase
    {
        internal int Changes;

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<SpatialWorkspace>(0);
            builder.AddAttribute(1, "Season", season);
            builder.AddAttribute(2, "Changed", EventCallback.Factory.Create(this, () => Changes++));
            builder.CloseComponent();
        }
    }
}
