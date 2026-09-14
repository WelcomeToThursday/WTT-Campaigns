using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using MudBlazor.Services;
using WTT.Campaigns.Server.Seasons;
using WTT.Campaigns.Server.Web.Authoring;
using WTT.Campaigns.Server.Web.Components;
using WTT.Campaigns.Shared.Native;
using WTT.Campaigns.Shared.Seasons;
using WTT.Campaigns.Shared.Story;

// Render the real Creator components in memory; no host, SPT runtime or profile is opened.
await using var services = new ServiceCollection()
    .AddLogging()
    .AddSingleton<IJSRuntime, OfflineJsRuntime>()
    .AddMudServices()
    .AddSingleton((SeasonContentService)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(SeasonContentService)))
    .AddSingleton((SeasonRepository)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(SeasonRepository)))
    .BuildServiceProvider();
var count = 0;
void Check(bool passed, string message)
{
    if (!passed)
    {
        throw new Exception(message);
    }
    count++;
}
foreach (var operation in new[] { "remove", "switch", "clear quest", "reassign quest" })
{
    await using var renderer = new EditorRenderer(services);
    var quest = NativeQuestAuthoring.Create();
    var otherQuest = NativeQuestAuthoring.Create();
    var membership = new StoryQuest { QuestId = (string)quest.Id! };
    var otherMembership = new StoryQuest { QuestId = (string)otherQuest.Id! };
    var action = new StoryAction
    {
        Id = StoryAuthoring.NewId(),
        Type = StoryActionType.AcceptQuest,
        QuestId = membership.QuestId,
    };
    var line = new StoryDialogLine
    {
        Id = StoryAuthoring.NewId(),
        Text = "Keep this reply",
        Actions = [action],
    };
    var dialog = new StoryDialog { Id = StoryAuthoring.NewId(), Lines = [line] };
    var unrelated = new StoryDialog { Id = StoryAuthoring.NewId(), Lines = [new() { Id = StoryAuthoring.NewId(), Text = "Unrelated" }] };
    var season = new SeasonDefinition
    {
        Story = new() { Dialogs = [dialog, unrelated], Quests = [membership, otherMembership] },
        Quests = [quest, otherQuest],
    };
    var host = new EditorHost(season, quest, membership);
    await renderer.Dispatcher.InvokeAsync(async () =>
    {
        await renderer.Mount(host);
        bool Visible(object record)
        {
            return renderer.Components<StoryFields>().Any(c => ReferenceEquals(c.Component.Value, record));
        }
        Check(!Visible(dialog) && !Visible(unrelated), "Quest events show links without embedding conversation forms");
        var flow = renderer.Components<QuestStoryFlowEditor>().Single();
        Check(
            renderer.Text(flow.Id).Contains("Keep this reply") && !renderer.Text(flow.Id).Contains("Unrelated"),
            "Quest links include only related conversations"
        );
        await renderer.DispatchEventAsync(
            renderer.Event(flow.Id, "button", "Open in Conversations", "onclick"),
            null,
            new MouseEventArgs()
        );
        Check(host.Navigation == "Story/" + dialog.Id, "Conversation opens in its permanent workspace");
        var writer = renderer.Components<ConversationWriter>().Single();
        await renderer.DispatchEventAsync(renderer.Event(writer.Id, "button", "Effects", "onclick"), null, new MouseEventArgs());
        if (operation == "remove")
        {
            var fields = renderer.Components<StoryFields>().Single(c => ReferenceEquals(c.Component.Value, line));
            await renderer.DispatchEventAsync(renderer.Event(fields.Id, "button", "Remove", "onclick"), null, new MouseEventArgs());
            Check(line.Actions.Count == 0, "Remove deletes only the selected action");
        }
        else if (operation == "switch")
        {
            var field = renderer
                .Components<StoryValue>()
                .Single(c => ReferenceEquals(c.Component.Owner, action) && c.Component.Field == "Type");
            await renderer.DispatchEventAsync(
                renderer.Event(field.Id, "select", "", "onchange"),
                null,
                new ChangeEventArgs { Value = "SwitchDialog" }
            );
            Check(
                action.Type == StoryActionType.SwitchDialog && action.QuestId == "",
                "Switch updates action type and clears its obsolete quest target"
            );
        }
        else
        {
            var picker = renderer.Components<ContentPicker>().Single(c => c.Component.Kind == "ownedquests");
            var target = operation == "clear quest" ? "" : otherMembership.QuestId;
            await picker.Component.ValueChanged.InvokeAsync(target);
            Check(action.QuestId == target, "Quest target changes through the editor callback");
        }
        Check(Visible(dialog) && Visible(line), $"Dialogue remains editable after {operation} removes its last quest reference");
        Check(
            season.Story.Dialogs.Count == 2 && dialog.Lines.Single() == line && line.Text == "Keep this reply",
            "Action edits preserve conversations and dialogue text"
        );
        var remaining = renderer.Components<StoryFields>().Single(c => ReferenceEquals(c.Component.Value, line));
        await renderer.DispatchEventAsync(renderer.Event(remaining.Id, "button", "Add action", "onclick"), null, new MouseEventArgs());
        Check(
            line.Actions.Count == (operation == "remove" ? 1 : 2) && Visible(line),
            "Further action editing works after losing the quest reference"
        );
        Check(
            SeasonCompiler.Copy(season).Story!.Dialogs.Single(d => d.Id == dialog.Id).Lines.Single().Actions.Count == line.Actions.Count,
            "Edited dialogue survives draft serialization"
        );

        host.Show(season, otherQuest, otherMembership);
        flow = renderer.Components<QuestStoryFlowEditor>().Single();
        Check(
            renderer.Text(flow.Id).Contains("Keep this reply") == (operation == "reassign quest"),
            "Quest links follow current references"
        );
        host.Show(season, quest, membership);
        flow = renderer.Components<QuestStoryFlowEditor>().Single();
        Check(!renderer.Text(flow.Id).Contains("Keep this reply"), "Reopening the quest rediscovers only its current references");
        action.QuestId = membership.QuestId;
        if (!line.Actions.Contains(action))
        {
            line.Actions.Add(action);
        }
        host.Show(season, quest, membership);
        flow = renderer.Components<QuestStoryFlowEditor>().Single();
        Check(renderer.Text(flow.Id).Contains("Keep this reply"), "Newly linked conversations appear while editing");
        var otherDraft = SeasonCompiler.Copy(season);
        otherDraft.Story!.Dialogs.Single(d => d.Id == dialog.Id).Lines.Single().Actions.Clear();
        host.Show(otherDraft, otherDraft.Quests[0], otherDraft.Story.Quests[0]);
        flow = renderer.Components<QuestStoryFlowEditor>().Single();
        Check(!renderer.Text(flow.Id).Contains("Keep this reply"), "Retained IDs do not leak into a different draft");
    });
}
await using (var renderer = new EditorRenderer(services))
{
    var season = new SeasonDefinition { Story = new() };
    var chapter = new StoryChapter { Id = StoryAuthoring.NewId(), Name = "First contact" };
    season.Story.Chapters.Add(chapter);
    var first = QuestStoryFlow.AddQuest(season, chapter.Id);
    var second = QuestStoryFlow.AddQuest(season, chapter.Id);
    NativeQuestAuthoring.QuestText(first, "name", "First quest");
    NativeQuestAuthoring.QuestText(second, "name", "Second quest");
    var views = new Dictionary<string, string>();
    var host = new WorkspaceHost(season, chapter.Id, views);
    await renderer.Dispatcher.InvokeAsync(async () =>
    {
        await renderer.Mount(host);
        var workspace = renderer.Components<StoryWorkspace>().Single();
        var questEditor = renderer.Components<QuestWorkspace>().Single();
        await renderer.DispatchEventAsync(
            renderer.Event(questEditor.Id, "button", "Unlock requirements", "onclick"),
            null,
            new MouseEventArgs()
        );
        Check(
            renderer.Components<NativeObjectiveFields>().All(c => c.Component.Value.ConditionType == "Level"),
            "Unlock step contains only start requirements"
        );
        await renderer.DispatchEventAsync(renderer.Event(questEditor.Id, "button", "Objectives", "onclick"), null, new MouseEventArgs());
        Check(
            renderer.Components<NativeObjectiveFields>().All(c => c.Component.Value.ConditionType != "Level"),
            "Objective step keeps start requirements separate"
        );
        await renderer.DispatchEventAsync(renderer.Event(workspace.Id, "button", "Second quest", "onclick"), null, new MouseEventArgs());
        questEditor = renderer.Components<QuestWorkspace>().Single();
        Check(renderer.Text(questEditor.Id).Contains("Quest identity"), "A different quest opens at Basics");
        await renderer.DispatchEventAsync(renderer.Event(workspace.Id, "button", "First quest", "onclick"), null, new MouseEventArgs());
        questEditor = renderer.Components<QuestWorkspace>().Single();
        Check(renderer.Text(questEditor.Id).Contains("What must the player do?"), "Returning to a quest restores its editing step");
        var snapshot = Newtonsoft.Json.JsonConvert.SerializeObject(season);
        await renderer.DispatchEventAsync(renderer.Event(questEditor.Id, "button", "Preview", "onclick"), null, new MouseEventArgs());
        Check(Newtonsoft.Json.JsonConvert.SerializeObject(season) == snapshot, "Opening quest preview leaves all authored data unchanged");
        await renderer.DispatchEventAsync(renderer.Event(workspace.Id, "button", "First contact", "onclick"), null, new MouseEventArgs());
        Check(!renderer.Components<QuestWorkspace>().Any(), "Chapter selection opens chapter settings without nested quest tabs");
    });
}
await using (var renderer = new EditorRenderer(services))
{
    var season = new SeasonDefinition { Story = new() };
    var dialog = StoryAuthoring.AddConversation(season, "111111111111111111111111", true);
    var host = new ConversationWorkspaceHost(season, dialog.Id);
    await renderer.Dispatcher.InvokeAsync(async () =>
    {
        await renderer.Mount(host);
        var workspace = renderer.Components<StoryWorkspace>().Single();
        var before = Newtonsoft.Json.JsonConvert.SerializeObject(season);
        await renderer.DispatchEventAsync(renderer.Event(workspace.Id, "button", "Delete", "onclick"), null, new MouseEventArgs());
        var panel = renderer.Components<ConversationDeletePanel>().Single();
        Check(Newtonsoft.Json.JsonConvert.SerializeObject(season) == before, "Delete opens a preview before changing conversation content");
        await renderer.DispatchEventAsync(renderer.Event(panel.Id, "button", "Cancel", "onclick"), null, new MouseEventArgs());
        Check(
            !renderer.Components<ConversationDeletePanel>().Any() && Newtonsoft.Json.JsonConvert.SerializeObject(season) == before,
            "Cancel dismisses deletion without changes"
        );
        await renderer.DispatchEventAsync(renderer.Event(workspace.Id, "button", "Delete", "onclick"), null, new MouseEventArgs());
        panel = renderer.Components<ConversationDeletePanel>().Single();
        await renderer.DispatchEventAsync(
            renderer.Event(panel.Id, "button", "Delete conversation and entry points", "onclick"),
            null,
            new MouseEventArgs()
        );
        Check(
            season.Story.Dialogs.Count == 0 && season.Story.EntryPoints.Count == 0,
            "Confirm deletes a template conversation without manual entry-point cleanup"
        );
        Check(
            !renderer.Components<ConversationWriter>().Any() && !renderer.Components<ConversationDeletePanel>().Any(),
            "After deletion no editor retains the removed conversation"
        );
        Check(host.Changes == 1 && host.SelectedId == "", "Deletion marks the draft changed and clears the parent selection");
    });
}
await MapLayoutUiChecks.Run(services, Check);
await TraderOfferUiChecks.Run(Check);
WTT.Campaigns.Web.Tests.EncounterChecks.Run(Check);
WTT.Campaigns.Web.Tests.AuthoringMapSessionChecks.Run(Check);
Console.WriteLine($"PASS {count} Creator component assertions");

sealed class EditorHost(SeasonDefinition season, NativeQuest quest, StoryQuest membership) : ComponentBase
{
    public string Navigation { get; private set; } = "";
    private StoryDialog? _dialog;

    public void Show(SeasonDefinition definition, NativeQuest selected, StoryQuest storyQuest)
    {
        _dialog = null;
        season = definition;
        quest = selected;
        membership = storyQuest;
        StateHasChanged();
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<MudBlazor.MudPopoverProvider>(0);
        builder.CloseComponent();
        if (_dialog != null)
        {
            builder.OpenComponent<ConversationWriter>(1);
            builder.AddAttribute(2, "Season", season);
            builder.AddAttribute(3, "Dialog", _dialog);
            builder.AddAttribute(4, "Changed", EventCallback.Factory.Create(this, () => { }));
            builder.CloseComponent();
        }
        else
        {
            builder.OpenComponent<QuestStoryFlowEditor>(5);
            builder.AddAttribute(6, "Season", season);
            builder.AddAttribute(7, "Quest", quest);
            builder.AddAttribute(8, "Membership", membership);
            builder.AddAttribute(9, "Changed", EventCallback.Factory.Create(this, () => { }));
            builder.AddAttribute(
                10,
                "Navigate",
                EventCallback.Factory.Create<string>(
                    this,
                    path =>
                    {
                        Navigation = path;
                        _dialog = season.Story!.Dialogs.FirstOrDefault(d => "Story/" + d.Id == path);
                    }
                )
            );
            builder.CloseComponent();
        }
    }
}

sealed class WorkspaceHost(SeasonDefinition season, string chapterId, Dictionary<string, string> views) : ComponentBase
{
    private string _child = "";

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<MudBlazor.MudPopoverProvider>(0);
        builder.CloseComponent();
        builder.OpenComponent<StoryWorkspace>(1);
        builder.AddAttribute(2, "Season", season);
        builder.AddAttribute(3, "Section", "Chapters");
        builder.AddAttribute(4, "FocusId", chapterId);
        builder.AddAttribute(5, "FocusChildId", _child);
        builder.AddAttribute(6, "QuestViews", views);
        builder.AddAttribute(7, "Changed", EventCallback.Factory.Create(this, () => { }));
        builder.AddAttribute(8, "ChildSelectionChanged", EventCallback.Factory.Create<string>(this, id => _child = id));
        builder.CloseComponent();
    }
}

sealed class ConversationWorkspaceHost(SeasonDefinition season, string selectedId) : ComponentBase
{
    public string SelectedId { get; private set; } = selectedId;
    public int Changes { get; private set; }
    private string _child = "";

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<MudBlazor.MudPopoverProvider>(0);
        builder.CloseComponent();
        builder.OpenComponent<StoryWorkspace>(1);
        builder.AddAttribute(2, "Season", season);
        builder.AddAttribute(3, "Section", "Conversations");
        builder.AddAttribute(4, "FocusId", SelectedId);
        builder.AddAttribute(5, "FocusChildId", _child);
        builder.AddAttribute(6, "SelectionChanged", EventCallback.Factory.Create<string>(this, id => SelectedId = id));
        builder.AddAttribute(7, "Changed", EventCallback.Factory.Create(this, () => Changes++));
        builder.AddAttribute(8, "ChildSelectionChanged", EventCallback.Factory.Create<string>(this, id => _child = id));
        builder.CloseComponent();
    }
}

sealed class EditorRenderer(IServiceProvider services) : Renderer(services, NullLoggerFactory.Instance, new EditorActivator())
{
    private int _root;
    public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();

    protected override void HandleException(Exception exception)
    {
        throw new InvalidOperationException("Creator render failed", exception);
    }

    protected override Task UpdateDisplayAsync(in RenderBatch renderBatch)
    {
        return Task.CompletedTask;
    }

    public Task Mount(IComponent component)
    {
        _root = AssignRootComponentId(component);
        return RenderRootComponentAsync(_root);
    }

    public IEnumerable<(int Id, T Component)> Components<T>()
        where T : IComponent
    {
        return Descendants(_root).Where(c => c.Component is T).Select(c => (c.Id, (T)c.Component));
    }

    private IEnumerable<(int Id, IComponent Component)> Descendants(int id)
    {
        var frames = GetCurrentRenderTreeFrames(id);
        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames.Array[i];
            if (frame.FrameType != RenderTreeFrameType.Component)
            {
                continue;
            }
            yield return (frame.ComponentId, frame.Component);
            foreach (var child in Descendants(frame.ComponentId))
            {
                yield return child;
            }
        }
    }

    public ulong Event(int id, string element, string text, string eventName)
    {
        var frames = GetCurrentRenderTreeFrames(id);
        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames.Array[i];
            if (frame.FrameType != RenderTreeFrameType.Element || frame.ElementName != element)
            {
                continue;
            }
            var subtree = frames.Array.Skip(i + 1).Take(frame.ElementSubtreeLength - 1).ToArray();
            var label = string.Concat(
                subtree.Select(f =>
                    f.FrameType == RenderTreeFrameType.Text ? f.TextContent
                    : f.FrameType == RenderTreeFrameType.Markup ? f.MarkupContent
                    : ""
                )
            );
            if (text.Length > 0 && !label.StartsWith(text, StringComparison.Ordinal))
            {
                continue;
            }
            return subtree.First(f => f.FrameType == RenderTreeFrameType.Attribute && f.AttributeName == eventName).AttributeEventHandlerId;
        }
        throw new Exception($"Missing {element} '{text}'");
    }

    public string? ImageSource(int id)
    {
        var frames = GetCurrentRenderTreeFrames(id);
        return frames
            .Array.Take(frames.Count)
            .Where(f => f.FrameType == RenderTreeFrameType.Attribute && f.AttributeName == "src")
            .Select(f => f.AttributeValue?.ToString())
            .FirstOrDefault();
    }

    public string Text(int id)
    {
        var frames = GetCurrentRenderTreeFrames(id);
        return string.Concat(
            frames
                .Array.Take(frames.Count)
                .Select(f =>
                    f.FrameType == RenderTreeFrameType.Text ? f.TextContent
                    : f.FrameType == RenderTreeFrameType.Markup ? f.MarkupContent
                    : ""
                )
        );
    }
}

// Substitute installed-content lookup and browser JS only. All story editors,
// tooltips and authoring event handlers are production components.
sealed class EditorActivator : IComponentActivator
{
    public IComponent CreateInstance(Type componentType)
    {
        return componentType == typeof(ContentPicker) ? new OfflineContentPicker()
            : componentType == typeof(AssetPicker) ? new OfflineAssetPicker()
            : (IComponent)Activator.CreateInstance(componentType)!;
    }
}

sealed class OfflineContentPicker : ContentPicker
{
    protected override void BuildRenderTree(RenderTreeBuilder builder) { }
}

sealed class OfflineAssetPicker : AssetPicker
{
    protected override void BuildRenderTree(RenderTreeBuilder builder) { }
}

sealed class OfflineJsRuntime : IJSRuntime
{
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
    {
        return ValueTask.FromResult(default(TValue)!);
    }

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        return InvokeAsync<TValue>(identifier, args);
    }
}
