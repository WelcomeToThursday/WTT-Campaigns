using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor.Services;
using Newtonsoft.Json.Linq;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Locales;
using SPTarkov.Server.Core.Utils.Json;
using WTT.Campaigns.Server.Seasons;
using WTT.Campaigns.Server.Web.Authoring;
using WTT.Campaigns.Server.Web.Components;
using WTT.Campaigns.Shared.Native;
using WTT.Campaigns.Shared.Seasons;
using WTT.Campaigns.Shared.Story;

// Offline rendering of the real editor components; no game/server process or profiles.
var root = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
var output = Path.Combine(root, "Research", "EditorRendering");
Directory.CreateDirectory(output);
var tables = Activator.CreateInstance<TemplateTable>();
foreach (
    var property in typeof(TemplateTable).GetProperties().Where(p => p.CanWrite && p.PropertyType.GetConstructor(Type.EmptyTypes) != null)
)
    property.SetValue(tables, Activator.CreateInstance(property.PropertyType));
var localeTable = Activator.CreateInstance<LocaleTable>();
typeof(LocaleTable)
    .GetProperty("Global")!
    .SetValue(localeTable, new Dictionary<string, LazyLoad<GlobalLocaleDictionary>> { ["en"] = new(() => new(), cacheValue: false) });
var content = new SeasonContentService(
    null!,
    tables,
    new TradersTable(),
    null!,
    new LocaleService(null!, localeTable, null!),
    localeTable,
    null!,
    null!,
    null!,
    [],
    null!,
    null!
);
var services = new ServiceCollection().AddLogging();
services.AddMudServices();
services.AddSingleton(content);
services.AddSingleton<IJSRuntime, OfflineJs>();
services.AddSingleton<NavigationManager, OfflineNavigation>();
await using var provider = services.BuildServiceProvider();
await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
var season = new SeasonDefinition { Story = new() };
season.Story.Variables.Add(
    new()
    {
        Id = "phase",
        Scope = StoryVariableScope.Profile,
        InitialValue = 0,
    }
);
var quest = NativeQuestAuthoring.Create();
var counter = NativeQuestAuthoring.Condition("CounterCreator");
counter.Value = 5;
var kills = NativeQuestAuthoring.Condition("Kills");
counter.Counter!.Conditions.Add(kills);
var pages = new Dictionary<string, string>();
pages["counter"] = await Render<NativeObjectiveFields>(
    new()
    {
        ["Season"] = season,
        ["Quest"] = quest,
        ["Value"] = counter,
    }
);
Require(Count(pages["counter"], "Objective text") == 1, "Counter filters must not render another objective-text field");
Require(Count(pages["counter"], "Required objective") == 2, "Only the parent counter has a required-objective control and its tooltip");
Require(
    pages["counter"].Contains("Combat restrictions") && pages["counter"].Contains("Distance (metres)"),
    "Nested combat groups and distance units render"
);
pages["handover"] = await Render<NativeObjectiveFields>(
    new()
    {
        ["Season"] = season,
        ["Quest"] = quest,
        ["Value"] = NativeQuestAuthoring.Condition("HandoverItem"),
    }
);
Require(
    pages["handover"].Contains("Accepted item quality") && pages["handover"].Contains("consumed"),
    "Handover groups and specific help render"
);
pages["prerequisite"] = await Render<NativeObjectiveFields>(
    new()
    {
        ["Season"] = season,
        ["Quest"] = quest,
        ["Value"] = NativeQuestAuthoring.Condition("Quest"),
    }
);
Require(pages["prerequisite"].Contains("Accepted prerequisite statuses"), "Prerequisites offer multiple accepted statuses");
NativeQuestAuthoring.AddQuestReward(quest, "Experience");
NativeQuestAuthoring.AddQuestReward(quest, "TraderStanding", "Fail");
pages["rewards"] = await Render<QuestRewards>(new() { ["Quest"] = quest });
Require(
    pages["rewards"].Contains("On completion") && pages["rewards"].Contains("On acceptance") && pages["rewards"].Contains("On failure"),
    "All three reward stages render"
);
var condition = new StoryCondition
{
    Type = "VariableValue",
    Target = "phase",
    Value = 3,
};
pages["variable-condition"] = await Render<StoryFields>(new() { ["Season"] = season, ["Value"] = condition });
Require(
    pages["variable-condition"].Contains("Variable threshold") && pages["variable-condition"].Contains("Profile scope"),
    "Selected variable context reaches rendered help"
);
condition.Type = "TraderReputation";
pages["reputation-condition"] = await Render<StoryFields>(new() { ["Season"] = season, ["Value"] = condition });
Require(
    pages["reputation-condition"].Contains("Reputation threshold") && !pages["reputation-condition"].Contains("Variable threshold"),
    "Type changes replace the rendered label and help"
);
season.Zones.Add(
    new()
    {
        Id = "111111111111111111111111",
        Name = "Camp entrance",
        Location = "woods",
        Scene = "woods_main",
    }
);
pages["zones"] = await Render<SpatialWorkspace>(new() { ["Season"] = season });
Require(
    pages["zones"].Contains("Create in raid") && pages["zones"].Contains("Camp entrance"),
    "Spatial workspace renders capture and geometry controls"
);
var visit = NativeQuestAuthoring.Condition("VisitPlace");
pages["zone-objective"] = await Render<NativeObjectiveFields>(
    new()
    {
        ["Season"] = season,
        ["Quest"] = quest,
        ["Value"] = visit,
    }
);
Require(
    pages["zone-objective"].Contains("Camp entrance") && pages["zone-objective"].Contains("Create in raid"),
    "Native objectives expose compatible authored zones and capture requests"
);
pages["zone-binding"] = await Render<StoryFields>(
    new()
    {
        ["Season"] = season,
        ["Value"] = new StoryRaidBinding
        {
            Id = "binding",
            Kind = "Trigger",
            Location = "woods",
        },
    }
);
Require(
    pages["zone-binding"].Contains("Trigger zone") && pages["zone-binding"].Contains("Pick in raid"),
    "Story bindings expose both zone and scene-target capture"
);
var chapter = new StoryChapter { Id = "333333333333333333333333", Name = "First contact" };
season.Story.Chapters.Add(chapter);
season.Quests.Add(quest);
NativeQuestAuthoring.QuestText(quest, "name", "A small favor");
NativeQuestAuthoring.QuestText(
    quest,
    "description",
    "Prapor needs supplies for a stranded patrol. Bring the medical supplies back to him."
);
var membership = new StoryQuest { QuestId = (string)quest.Id!, ChapterId = chapter.Id };
season.Story.Quests.Add(membership);
quest.English()[counter.Id] = "Eliminate five Scavs on Customs";
quest.Conditions.AvailableForFinish.Add(counter);
var note = QuestStoryFlow.AddNote(season, membership, "Success");
note.Text = "Prapor received the supplies. We have made our first contact.";
var conversation = StoryAuthoring.AddConversation(season, "444444444444444444444444", true, membership.QuestId);
conversation.Lines[0].Text = "A patrol is waiting on medical supplies. Can you help?";
pages["chapter-tree"] = await Render<StoryWorkspace>(
    new()
    {
        ["Season"] = season,
        ["Section"] = "Chapters",
        ["FocusId"] = chapter.Id,
        ["FocusChildId"] = membership.QuestId,
    }
);
pages["quest-objectives"] = await Render<StoryWorkspace>(
    new()
    {
        ["Season"] = season,
        ["Section"] = "Chapters",
        ["FocusId"] = chapter.Id,
        ["FocusChildId"] = membership.QuestId,
        ["QuestViews"] = new Dictionary<string, string> { [membership.QuestId] = "Objectives" },
    }
);
pages["quest-events"] = await Render<QuestStoryFlowEditor>(
    new()
    {
        ["Season"] = season,
        ["Quest"] = quest,
        ["Membership"] = membership,
    }
);
pages["conversation-writer"] = await Render<StoryWorkspace>(
    new()
    {
        ["Season"] = season,
        ["Section"] = "Conversations",
        ["FocusId"] = conversation.Id,
    }
);
Require(
    pages["chapter-tree"].Contains("Current editing location") && pages["chapter-tree"].Contains("Unlock requirements"),
    "Quest navigation has chapter context and separate unlock step"
);
Require(
    pages["quest-events"].Contains("completed and handed in") && !pages["quest-events"].Contains("Conversation lines"),
    "Story event overview describes lifecycle without nested conversation editing"
);
Require(
    pages["conversation-writer"].Contains("Conversation outline") && pages["conversation-writer"].Contains("Add connected reply"),
    "Conversation writing keeps flow alongside connected reply controls"
);
var theme = File.ReadAllText(
    Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".nuget/packages/sptarkov.server.web/4.1.0/staticwebassets/css/spt-theme.css"
    )
);
var css = File.ReadAllText(Path.Combine(root, "Server/wwwroot/creator.css"));
foreach (var (name, markup) in pages)
    File.WriteAllText(
        Path.Combine(output, name + ".html"),
        "<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>Editor rendering: "
            + name
            + "</title><style>"
            + theme
            + css
            + "</style></head><body><main class=\"season-creator\"><div class=\"editor-body\" style=\"max-width:1640px;margin:auto\">"
            + markup
            + "</div></main></body></html>"
    );
Console.WriteLine($"PASS: rendered {pages.Count} real editor component scenarios into {output}");

async Task<string> Render<T>(Dictionary<string, object?> parameters)
    where T : IComponent =>
    await renderer.Dispatcher.InvokeAsync(async () =>
        (await renderer.RenderComponentAsync<T>(ParameterView.FromDictionary(parameters))).ToHtmlString()
    );
static int Count(string value, string text) => value.Split(text).Length - 1;
static void Require(bool result, string description)
{
    if (!result)
        throw new Exception(description);
}

sealed class OfflineJs : IJSRuntime
{
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
        ValueTask.FromResult(default(TValue)!);
}

sealed class OfflineNavigation : NavigationManager
{
    public OfflineNavigation()
    {
        Initialize("http://localhost/", "http://localhost/editor-preview");
    }

    protected override void NavigateToCore(string uri, bool forceLoad) { }
}
