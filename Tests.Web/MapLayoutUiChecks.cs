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
        var season = new SeasonDefinition
        {
            FormatVersion = 4,
            MapLayouts = new() { map },
        };
        var host = new MapHost(season);
        await using var renderer = new EditorRenderer(services);
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.Mount(host);
            var component = renderer.Components<MapLayoutWorkspace>().Single();
            check(
                renderer.Text(component.Id).Contains("Building route") && renderer.Text(component.Id).Contains("Walkthrough requires"),
                "Creator shows incomplete saved layouts and route readiness"
            );
            await renderer.DispatchEventAsync(
                renderer.Event(component.Id, "input", "", "onchange"),
                null,
                new ChangeEventArgs { Value = "Hallway escape" }
            );
            check(map.Name == "Hallway escape" && host.Changes == 1, "Layout rename reaches the draft change handler");
            await renderer.DispatchEventAsync(
                renderer.Event(component.Id, "button", "Duplicate layout", "onclick"),
                null,
                new MouseEventArgs()
            );
            check(season.MapLayouts.Count == 2 && host.Changes == 2, "Creator duplicates layouts through the production component");
            check(
                !MapLayoutRules.OwnedIds(map).Intersect(MapLayoutRules.OwnedIds(season.MapLayouts[1])).Any(),
                "Creator duplication gives route records independent identities"
            );
            season.MapLayouts[1].Checkpoints[0].Position.X = 9;
            check(map.Checkpoints[0].Position.X == 0, "Duplicated layout geometry is independent");
            await renderer.DispatchEventAsync(
                renderer.Event(component.Id, "button", "Delete layout", "onclick"),
                null,
                new MouseEventArgs()
            );
            check(
                season.MapLayouts.Count == 1 && season.MapLayouts[0].Name == "Hallway escape copy" && host.Changes == 3,
                "Deleting a layout keeps the independent copy and signals persistence"
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
}
