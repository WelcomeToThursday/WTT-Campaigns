using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor.Services;
using Newtonsoft.Json;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Image;
using SPTarkov.Server.Core.Services.Locales;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Json;
using WTT.Campaigns.Server.Seasons;
using WTT.Campaigns.Server.Web.Authoring;
using WTT.Campaigns.Server.Web.Components;
using WTT.Campaigns.Shared.Authoring;
using WTT.Campaigns.Shared.Seasons;
using Path = System.IO.Path;

internal static class TraderOfferUiChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "campaign-offer-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "creator"));
        var season = new SeasonDefinition { Id = "000000000000000000000001", Name = "Trader authoring fixture" };
        File.WriteAllText(Path.Combine(directory, "creator", "legacy.json"), JsonConvert.SerializeObject(season));
        try
        {
            var repository = (SeasonRepository)
                Activator.CreateInstance(
                    typeof(SeasonRepository),
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    null,
                    new object[] { directory },
                    null
                )!;
            var json = new JsonUtil([new SptJsonConverterRegistrator()]);
            var table = JsonConvert.DeserializeObject<TemplateTable>("{}")!;
            var templates = new Dictionary<MongoId, TemplateItem>();
            typeof(TemplateTable).GetProperty(nameof(TemplateTable.Items))!.SetValue(table, templates);
            const string weapon = "000000000000000000000011",
                scope = "000000000000000000000012";
            templates[new MongoId(weapon)] = json.Deserialize<TemplateItem>(
                "{\"_id\":\""
                    + weapon
                    + "\",\"_type\":\"Item\",\"_parent\":\"000000000000000000000010\",\"_name\":\"Assembly\",\"_props\":{\"Width\":2,\"Height\":1,\"StackMaxSize\":1,\"Slots\":[{\"_name\":\"mod_scope\",\"_required\":false,\"_props\":{\"filters\":[{\"Filter\":[\""
                    + scope
                    + "\"]}]}}]}}"
            )!;
            templates[new MongoId(scope)] = json.Deserialize<TemplateItem>(
                "{\"_id\":\""
                    + scope
                    + "\",\"_type\":\"Item\",\"_parent\":\"000000000000000000000010\",\"_name\":\"Scope\",\"_props\":{\"Width\":1,\"Height\":1,\"StackMaxSize\":1}}"
            )!;
            var locale = new LocaleService(
                null!,
                new LocaleTable
                {
                    Global = new() { ["en"] = new(() => new GlobalLocaleDictionary(), cacheValue: false) },
                    Menu = [],
                    Languages = [],
                },
                null!
            );
            var traders = new TradersTable();
            const string traderId = "54cb50c76803fa8b248b4571",
                installedId = "000000000000000000000041";
            var trader = json.Deserialize<Trader>(
                "{\"base\":{\"_id\":\""
                    + traderId
                    + "\",\"nickname\":\"Fixture trader\",\"name\":\"Fixture trader\"},\"assort\":{\"items\":[],\"barter_scheme\":{},\"loyal_level_items\":{}},\"dialogue\":{},\"questassort\":{}}"
            )!;
            trader.Assort = CampaignTraderStock.Create(
                new CampaignTraderOffer
                {
                    Id = installedId,
                    TraderId = traderId,
                    Name = "Assembly",
                    Stock = 245000,
                    UnlimitedStock = true,
                    PurchaseLimit = 3,
                    Items = [new() { Id = installedId, Template = weapon }],
                    Barter =
                    [
                        [new() { Template = scope, Count = 2 }],
                    ],
                },
                json
            );
            traders[new MongoId(traderId)] = trader;
            var imageRoutes = new ImageRouterService();
            var portraits = new TraderPortraitService(traders, imageRoutes, new FileUtil());
            check(portraits.Find(traderId) == null, "A trader without a registered portrait uses the placeholder");
            var portraitFile = Path.Combine(directory, "modded-trader-portrait.png");
            File.WriteAllBytes(
                portraitFile,
                Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl2nWQAAAAASUVORK5CYII=")
            );
            trader.Base.Avatar = "/custom-trader/Portrait.PNG";
            imageRoutes.AddRoute("/custom-trader/portrait", portraitFile);
            check(
                portraits.Find(traderId)?.Path == portraitFile,
                "Portrait lookup follows the registered mod route rather than guessing the trader's filename"
            );
            var catalogue = new TraderOfferCatalogue(
                table,
                traders,
                null!,
                locale,
                json,
                WTT.Campaigns.Tests.NativeItemHelperFixture.Create(table)
            );
            var previews = new ItemPreviewService(repository, catalogue);
            var content = new SeasonContentService(
                repository,
                table,
                traders,
                null!,
                locale,
                null!,
                null!,
                json,
                null!,
                [],
                catalogue,
                previews
            );
            await using var services = new ServiceCollection()
                .AddLogging()
                .AddSingleton<IJSRuntime, OfflineJsRuntime>()
                .AddMudServices()
                .AddSingleton(content)
                .AddSingleton(catalogue)
                .AddSingleton(previews)
                .AddSingleton(repository)
                .AddSingleton(portraits)
                .AddSingleton(
                    new WTT.Campaigns.Server.Editor.SceneContainerLoot(
                        JsonConvert.DeserializeObject<LocationTable>("{}")!,
                        null!,
                        null!,
                        json,
                        WTT.Campaigns.Tests.NativeItemHelperFixture.Create(table),
                        null!,
                        catalogue
                    )
                )
                .BuildServiceProvider();
            await ContainerLootUiChecks.Run(services, weapon, scope, check);
            await MissionLibraryUiChecks.Run(services, check);
            await using (var renderer = new EditorRenderer(services))
            {
                await renderer.Dispatcher.InvokeAsync(async () =>
                {
                    var host = new OfferHost(season);
                    await renderer.Mount(host);
                    var editor = renderer.Components<TraderOfferEditor>().Single();
                    async Task Click(string text) =>
                        await renderer.DispatchEventAsync(renderer.Event(editor.Id, "button", text, "onclick"), null, new MouseEventArgs());
                    check(
                        renderer.Text(editor.Id).Contains("Choose a trader") && !renderer.Text(editor.Id).Contains("Item assembly"),
                        "Trader selection is the starting point, without unrelated offer controls"
                    );
                    await Click("Fixture trader");
                    check(
                        season.TraderOffers.Count == 0 && renderer.Text(editor.Id).Contains("Installed offer"),
                        "Selecting a trader shows its existing assortment without mutating the draft"
                    );
                    var installedJson = json.Serialize(trader.Assort);
                    var rawItems = content.InstalledTraderOffers(traderId).Single().Items;
                    check(rawItems[0].ParentId == "hideout", "The icon regression fixture includes the native trader-only parent");
                    var previewRequest = new AuthoringRequest
                    {
                        ClientId = Guid.NewGuid().ToString("N"),
                        Enabled = true,
                        Preview = new() { Fingerprint = new string('a', 64), Ready = true },
                    };
                    previews.Exchange("fixture", "Fixture character", previewRequest);
                    // Reproduce a cached failure from the old installed-card path.
                    previews.Request(previewRequest.ClientId, "old-thumbnail", season.Id, rawItems);
                    var failedJob = previews.Exchange("fixture", "Fixture character", previewRequest).Preview!.Job!;
                    previewRequest.Preview.Result = new()
                    {
                        Id = failedJob.Id,
                        Key = failedJob.Key,
                        Errors = ["Critical MongoId error: incorrect length. Id: hideout"],
                    };
                    previews.Exchange("fixture", "Fixture character", previewRequest);
                    previewRequest.Preview.Result = null;
                    await renderer.DispatchEventAsync(
                        renderer.Event(editor.Id, "select", "Use cached images", "onchange"),
                        null,
                        new ChangeEventArgs { Value = previewRequest.ClientId }
                    );
                    var thumbnailJob = previews.Exchange("fixture", "Fixture character", previewRequest).Preview!.Job;
                    check(
                        thumbnailJob != null && thumbnailJob.Key != failedJob.Key,
                        "Browsing installed cards queues a fresh thumbnail despite the old cached render failure"
                    );
                    var thumbnailRoot = thumbnailJob!.Items.Single();
                    check(
                        thumbnailRoot.ParentId == null
                            && thumbnailRoot.SlotId == null
                            && thumbnailRoot.Location == null
                            && thumbnailRoot.Upd?.StackObjectsCount == 1
                            && thumbnailRoot.Upd.UnlimitedCount == null
                            && thumbnailRoot.Upd.BuyRestrictionMax == null
                            && thumbnailRoot.Upd.BuyRestrictionCurrent == null,
                        "Installed card rendering receives a detached single item without trader stock or purchase counters"
                    );
                    previewRequest.Preview.Result = new()
                    {
                        Id = thumbnailJob.Id,
                        Key = thumbnailJob.Key,
                        Verified = true,
                        Png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==",
                    };
                    previews.Exchange("fixture", "Fixture character", previewRequest);
                    await Task.Delay(2200);
                    var thumbnail = renderer.Components<ItemPreviewImage>().Single();
                    check(
                        renderer.ImageSource(thumbnail.Id) == $"/wtt-campaigns/creator/item-previews/{thumbnailJob.Key}.png"
                            && season.TraderOffers.Count == 0
                            && json.Serialize(trader.Assort) == installedJson,
                        "The assortment card displays its completed icon automatically before editing and leaves native stock intact"
                    );
                    await renderer.DispatchEventAsync(
                        renderer.Event(editor.Id, "select", "Use cached images", "onchange"),
                        null,
                        new ChangeEventArgs { Value = "" }
                    );
                    check(renderer.ImageSource(thumbnail.Id) != null, "Installed thumbnails remain visible in cached-image mode");
                    previewRequest.Enabled = false;
                    previews.Exchange("fixture", "Fixture character", previewRequest);
                    await Click("Edit assortment");
                    await Click("Edit offer");
                    check(
                        previews.Get(season.TraderOffers.Single().Items, season.Id).Key == thumbnailJob.Key,
                        "Editing an installed offer reuses the already generated card image"
                    );
                    check(
                        season.TraderOffers.Count == 1
                            && season.TraderOffers[0].Items[0].Template == weapon
                            && season.TraderOffers[0].TraderId == traderId
                            && season.TraderAssorts.Single().RemovedOffers.Contains(installedId),
                        "Editing an existing offer creates its campaign replacement under the chosen trader"
                    );
                    await Click("mod_scope");
                    await Click("Add to mod_scope");
                    check(
                        season.TraderOffers[0].Items.Count == 2 && season.TraderOffers[0].Items[1].SlotId == "mod_scope",
                        "Compatible catalogue attachment is added through the real editor event"
                    );
                    await Click("Undo");
                    check(season.TraderOffers[0].Items.Count == 1, "Assembly undo restores the earlier tree");
                    await Click("Redo");
                    check(season.TraderOffers[0].Items.Count == 2, "Assembly redo restores the added attachment");
                    await Click("Add payment alternative");
                    check(season.TraderOffers[0].Barter.Count == 2, "Payment alternatives are editable in the rendered workspace");
                    season.SeasonalRewards.Add(new() { Grants = [season.TraderOffers[0].Reference()] });
                    await Click("Delete offer");
                    check(
                        season.TraderOffers.Count == 1 && renderer.Text(editor.Id).Contains("Remove this offer's reward references"),
                        "Rendered deletion protects referenced offers and explains the reason"
                    );
                    check(renderer.Text(editor.Id).Contains("Needs verification"), "Offline assemblies are clearly marked unverified");
                    season.SeasonalRewards.Clear();
                    await Click("Delete offer");
                    check(season.TraderOffers.Count == 0, "Rendered deletion removes an unreferenced offer");
                    await Click("Undo");
                    check(season.TraderOffers.Count == 1, "Offer deletion is reversible");
                    await Click("Clear assortment…");
                    await Click("Cancel");
                    check(season.TraderOffers.Count == 1, "Cancelling assortment clear leaves the draft intact");
                    await Click("Clear assortment…");
                    await Click("Clear all offers");
                    check(
                        season.TraderOffers.Count == 0
                            && season.TraderAssorts.Single().ReplaceExisting
                            && renderer.Text(editor.Id).Contains("This assortment is empty"),
                        "Clearing an assortment starts an empty custom trader draft"
                    );
                    await Click("Undo");
                    check(
                        season.TraderOffers.Count == 1 && !season.TraderAssorts.Single().ReplaceExisting,
                        "Undo restores offers and assortment replacement policy together"
                    );
                    await Click("Add offer");
                    await Click("Use item for new offer");
                    check(
                        season.TraderOffers.Count == 2 && season.TraderOffers.All(o => o.TraderId == traderId),
                        "New offers inherit the selected trader automatically"
                    );
                    check(
                        trader.Assort.Items.Count == 1 && trader.Assort.Items[0].Id.ToString() == installedId,
                        "Draft editing and clearing never mutate the installed assortment"
                    );
                });
            }
            var standaloneSeason = new SeasonDefinition { Id = "000000000000000000000002" };
            await using (var standaloneRenderer = new EditorRenderer(services))
            {
                await standaloneRenderer.Dispatcher.InvokeAsync(async () =>
                {
                    await standaloneRenderer.Mount(new OfferHost(standaloneSeason, true));
                    var editor = standaloneRenderer.Components<TraderOfferEditor>().Single();
                    async Task Click(string text) =>
                        await standaloneRenderer.DispatchEventAsync(
                            standaloneRenderer.Event(editor.Id, "button", text, "onclick"),
                            null,
                            new MouseEventArgs()
                        );
                    await Click("Fixture trader");
                    await Click("Edit assortment");
                    check(
                        standaloneSeason.TraderOffers.Count == 1 && standaloneSeason.TraderAssorts.Single().ReplaceExisting,
                        "Standalone edit mode captures a complete independent installed assortment"
                    );
                    await Click("Edit offer");
                    check(
                        !standaloneRenderer.Text(editor.Id).Contains("Requires reward unlock"),
                        "Standalone offers omit campaign-only reward settings"
                    );
                    await Click("← Back to assortment");
                    await Click("Clear assortment…");
                    await Click("Clear all offers");
                    check(
                        standaloneSeason.TraderOffers.Count == 0 && season.TraderOffers.Count == 2,
                        "Standalone clear is independent from campaign draft changes"
                    );
                    await Click("Undo");
                    check(standaloneSeason.TraderOffers.Count == 1, "Standalone clear can be undone");
                });
            }
            // Static render of the production component for local visual review. This
            // starts no web host, SPT runtime, game client or profile session.
            await using var html = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
            var rendered = await html.Dispatcher.InvokeAsync(async () =>
                (
                    await html.RenderComponentAsync<TraderOfferEditor>(
                        ParameterView.FromDictionary(
                            new Dictionary<string, object?>
                            {
                                [nameof(TraderOfferEditor.Season)] = season,
                                [nameof(TraderOfferEditor.SelectedOffer)] = season.TraderOffers[0].Id,
                            }
                        )
                    )
                ).ToHtmlString()
            );
            check(
                rendered.Contains("assort-columns")
                    && rendered.Contains("Payment alternatives")
                    && rendered.Contains("aria-label=\"Image provider\""),
                "Production trader workspace renders all three working areas and connection control"
            );
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
            var styles = Path.Combine(root, "Server", "wwwroot", "creator.css");
            if (File.Exists(styles))
            {
                Directory.CreateDirectory(Path.Combine(root, "artifacts"));
                File.WriteAllText(
                    Path.Combine(root, "artifacts", "trader-offers-preview.html"),
                    "<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>Trader offers — offline fixture</title><style>body{margin:0;font:14px system-ui;background:#09090b;}"
                        + File.ReadAllText(styles)
                        + "</style></head><body><main class=\"season-creator\"><h1>Trader offers</h1><p>Offline fixture · production editor layout</p>"
                        + rendered
                        + "</main></body></html>"
                );
            }
        }
        finally
        {
            // Only the exact temporary fixture directory created above is removed.
            Directory.Delete(directory, true);
        }
    }

    private sealed class OfferHost(SeasonDefinition season, bool standalone = false) : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<TraderOfferEditor>(0);
            builder.AddAttribute(1, nameof(TraderOfferEditor.Season), season);
            builder.AddAttribute(2, nameof(TraderOfferEditor.Standalone), standalone);
            builder.CloseComponent();
        }
    }
}
