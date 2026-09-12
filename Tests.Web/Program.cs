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
        Check(Visible(dialog) && !Visible(unrelated), "Initially show only conversations referencing this quest");
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
        var flow = renderer.Components<QuestStoryFlowEditor>().Single();
        Check(renderer.Text(flow.Id).Contains("no longer references this quest"), "Detached conversation explains its availability");
        await renderer.DispatchEventAsync(
            renderer.Event(flow.Id, "button", "Open in Conversations", "onclick"),
            null,
            new MouseEventArgs()
        );
        Check(host.Navigation == "Story/" + dialog.Id, "Detached conversation links to its permanent editor");

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
        Check(Visible(dialog) == (operation == "reassign quest"), "Retained conversations do not leak into another quest");
        host.Show(season, quest, membership);
        Check(!Visible(dialog), "Reopening the quest rediscovers only its current references");
        action.QuestId = membership.QuestId;
        if (!line.Actions.Contains(action))
        {
            line.Actions.Add(action);
        }
        host.Show(season, quest, membership);
        Check(Visible(dialog), "Newly linked conversations appear while the quest editor is open");
        var otherDraft = SeasonCompiler.Copy(season);
        var copiedDialog = otherDraft.Story!.Dialogs.Single(d => d.Id == dialog.Id);
        copiedDialog.Lines.Single().Actions.Clear();
        host.Show(otherDraft, otherDraft.Quests[0], otherDraft.Story.Quests[0]);
        Check(!Visible(copiedDialog), "Retained IDs do not leak into a different draft with the same quest IDs");
    });
}
Console.WriteLine($"PASS {count} Creator component assertions");

sealed class EditorHost(SeasonDefinition season, NativeQuest quest, StoryQuest membership) : ComponentBase
{
    public string Navigation { get; private set; } = "";

    public void Show(SeasonDefinition definition, NativeQuest selected, StoryQuest storyQuest)
    {
        season = definition;
        quest = selected;
        membership = storyQuest;
        StateHasChanged();
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<MudBlazor.MudPopoverProvider>(0);
        builder.CloseComponent();
        builder.OpenComponent<QuestStoryFlowEditor>(1);
        builder.AddAttribute(2, "Season", season);
        builder.AddAttribute(3, "Quest", quest);
        builder.AddAttribute(4, "Membership", membership);
        builder.AddAttribute(5, "Changed", EventCallback.Factory.Create(this, () => { }));
        builder.AddAttribute(6, "Navigate", EventCallback.Factory.Create<string>(this, path => Navigation = path));
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
            if (text.Length > 0 && label != text)
            {
                continue;
            }
            return subtree.First(f => f.FrameType == RenderTreeFrameType.Attribute && f.AttributeName == eventName).AttributeEventHandlerId;
        }
        throw new Exception($"Missing {element} '{text}'");
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
        return componentType == typeof(ContentPicker) ? new OfflineContentPicker() : (IComponent)Activator.CreateInstance(componentType)!;
    }
}

sealed class OfflineContentPicker : ContentPicker
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
