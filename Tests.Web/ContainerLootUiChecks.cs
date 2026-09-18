using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using WTT.Campaigns.Server.Web.Components;
using WTT.Campaigns.Shared.Seasons;
using WTT.Campaigns.Shared.Spatial;

internal static class ContainerLootUiChecks
{
    public static async Task Run(IServiceProvider services, string item, string other, Action<bool, string> check)
    {
        check(typeof(WTT.Campaigns.Server.Web.Pages.MissionEditor).GetCustomAttributes<RouteAttribute>().Any(r => r.Template == "/wtt-campaigns/creator/missions"), "Mission editor has its own browser route");
        check(typeof(WTT.Campaigns.Server.Web.Pages.MissionEditor).GetCustomAttributes<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>().Any(a => a.Policy == "Administrator"), "Standalone mission editing retains administrator authorization");
        var container = new MapObjectEdit { Id = "crate", Name = "Crate", Operation = "Copy", Target = new() { Kind = "AssetContainer", Template = item }, Container = new() { Mode = "Fixed" } };
        var untouched = new MapObjectEdit { Id = "other", Operation = "Copy", Target = new() { Kind = "Container", Template = item } };
        var layout = new MapLayout { Id = "layout", Objects = [container, untouched, new() { Id = "prop", Target = new() { Kind = "Prop" } }] };
        var season = new SeasonDefinition { MapLayouts = [layout] };
        var host = new Host(season, layout);
        await using var renderer = new EditorRenderer(services);
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.Mount(host);
            var editor = renderer.Components<MissionContainerLoot>().Single().Component;
            async Task Call(string name, params object[] args) => await (Task)typeof(MissionContainerLoot).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(editor, args)!;
            check(container.Container.Contents.Count == 0 && untouched.Container == null, "Opening container loot does not create or change settings");
            await Call("AddItem", item);
            await Call("AddItem", other);
            check(container.Container.Contents.Count == 2 && host.Changes == 2, "Fixed loot additions notify the draft and preserve separate entries");
            var entry = container.Container.Contents[0];
            await Call("SetQuantity", entry, new ChangeEventArgs { Value = "8" });
            check(container.Container.Contents[0].Count == 8, "Quantity edits persist to the selected container");
            await Call("SetQuantity", container.Container.Contents[0], new ChangeEventArgs { Value = "10001" });
            check(container.Container.Contents[0].Count == 8 && host.Changes == 3, "Invalid quantity is rejected without marking the draft changed");
            await Call("SetChance", new ChangeEventArgs { Value = "35" });
            await Call("SetChance", new ChangeEventArgs { Value = "101" });
            check(container.Container.SpawnChance == 35, "Container spawn chance enforces its bounds");
            await Call("SetMode", "Empty");
            check(container.Container.Contents.Count == 2, "Changing loot mode retains saved fixed contents");
            await Call("SetMode", "Fixed");
            await Call("RemoveItem", container.Container.Contents[0]);
            check(container.Container.Contents.Count == 1 && container.Container.Contents[0].Template == other, "Removing a row changes only that fixed entry");
            await Call("SetLocked", new ChangeEventArgs { Value = true });
            check(!container.Container.Locked, "A container cannot be locked without a key");
            await Call("SetKey", item);
            check(container.Container.KeyTemplate == "", "A non-key item cannot become the lock key");
            check(untouched.Container == null && layout.Objects.Count == 3, "Editing loot leaves other placements untouched");
            check(season.FormatVersion >= 9, "Container settings raise the persisted layout format");
            check(renderer.Components<ItemPreviewImage>().Any(p => p.Component.Campaign == season.Id && p.Component.Items.Any(i => i.Template == other)), "Container rows use the assort image component with campaign and item identity");
            var saved = SeasonCompiler.Copy(season);
            check(saved.MapLayouts[0].Objects[0].Container!.Contents[0].Template == other, "Container loot survives the campaign serialization roundtrip");
        });
    }
    private sealed class Host(SeasonDefinition season, MapLayout layout) : ComponentBase
    {
        public int Changes { get; private set; }
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<MissionContainerLoot>(0);
            builder.AddAttribute(1, "Season", season);
            builder.AddAttribute(2, "Layout", layout);
            builder.AddAttribute(3, "Changed", EventCallback.Factory.Create(this, () => Changes++));
            builder.CloseComponent();
        }
    }
}
