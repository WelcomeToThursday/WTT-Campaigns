using UnityEngine.UIElements;
using WTT.Campaigns.Shared.Missions;
using WTT.Campaigns.Shared.Seasons;
using WTT.Campaigns.Shared.Spatial;
using ZLinq;

namespace WTT.Campaigns.Client.Authoring.Views;

/// <summary>Edits mission logic through the editor's existing undo and save transaction.</summary>
internal sealed class MissionEditorPanel
{
    private readonly Foldout _root;
    private readonly EditorToolkitDocument _document;
    private readonly Func<RaidEditorSession?> _session;
    private string _missionId = "";
    private long _version = -1;

    internal MissionEditorPanel(RaidEditorView view, VisualElement parent, Func<RaidEditorSession?> session)
    {
        _document = view.Document;
        _root = _document.Clone<Foldout>("InspectorSection");
        _root.text = "Mission events and objectives";
        _root.value = false;
        _session = session;
        parent.Add(_root);
        _root.RegisterValueChangedCallback(e =>
        {
            if (e.target == _root)
                Refresh();
        });
        _root
            .schedule.Execute(() =>
            {
                if (_root.value && _version != _session()?.ContentVersion)
                    Refresh();
            })
            .Every(250);
    }

    private void Change(Action action)
    {
        _session()?.Edit(_ => action());
        Refresh();
    }

    private void Refresh()
    {
        if (!_root.value)
            return;
        _root.contentContainer.Clear();
        var session = _session();
        _version = session?.ContentVersion ?? -1;
        if (session?.Definition == null)
            return;
        var missions = session
            .Definition.Missions.AsValueEnumerable()
            .Where(m => session.Definition.MapLayouts.AsValueEnumerable().Any(l => l.Id == m.LayoutId && l.Location == session.Location))
            .ToArray();
        var mission = missions.AsValueEnumerable().FirstOrDefault(m => m.Id == _missionId) ?? missions.AsValueEnumerable().FirstOrDefault();
        if (mission == null)
        {
            Message(_root, "Create a mission linked to this map in Creator first.");
            return;
        }
        _missionId = mission.Id;
        Choice(
            _root,
            "Mission",
            mission.Id,
            missions.AsValueEnumerable().Select(m => (m.Id, m.Name)).ToArray(),
            id =>
            {
                _missionId = id;
                Refresh();
            },
            false
        );
        var layout = session.Definition.MapLayouts.AsValueEnumerable().First(l => l.Id == mission.LayoutId);
        Toggle(_root, "Allow checkpoint retries", mission.CheckpointRetries, value => mission.CheckpointRetries = value);
        Message(
            _root,
            "Test checkpoints beside Playtest rehearses the selected layout with retries enabled. Use Test mission to include mission objectives and events."
        );
        foreach (var error in MissionLogicRules.Errors(mission, layout))
            Message(_root, error);
        Button(_root, "Add objective", () => mission.Objectives.Add(new() { Id = MissionAuthoring.NewId() }));
        Button(
            _root,
            "Add event",
            () =>
                mission.Events.Add(
                    new()
                    {
                        Id = MissionAuthoring.NewId(),
                        Source = MissionSignals.Start,
                        Actions = new() { new() },
                    }
                )
        );
        // AiTools already belongs to the vertical AiToolsScroll. A toolbar template
        // forces horizontal rows and a 38px height, even when its mode is changed.
        var scroll = _root.contentContainer;
        foreach (var o in mission.Objectives)
        {
            var group = _document.Clone<Foldout>("InspectorSection");
            group.text = o.Name;
            group.value = true;
            scroll.Add(group);
            Text(group, "Name", o.Name, value => o.Name = value);
            Choice(
                group,
                "Goal",
                o.Type,
                Names(MissionAuthoring.ObjectiveTypes),
                value =>
                {
                    o.Type = value;
                    var kind =
                        value is MissionObjective.Target or MissionObjective.Protect ? "Roster"
                        : value == MissionObjective.Survive ? "Encounter"
                        : o.TargetKind;
                    if (kind != o.TargetKind)
                    {
                        o.TargetKind = kind;
                        o.TargetIds.Clear();
                    }
                }
            );
            Toggle(group, "Active at start", o.OnStart, value => o.OnStart = value);
            Toggle(group, "Required", o.Required, value => o.Required = value);
            if (o.Type is MissionObjective.Target or MissionObjective.Protect)
            {
                var actors = layout
                    .Encounters.AsValueEnumerable()
                    .SelectMany(e => e.Waves)
                    .SelectMany(w => w.Roster)
                    .Where(r => r.Count == 1)
                    .Select(r => r.Id)
                    .ToHashSet();
                Choice(
                    group,
                    "Single actor",
                    o.TargetIds.Count == 1 ? o.TargetIds[0] : "",
                    MissionAuthoring.Targets(layout, "Roster").AsValueEnumerable().Where(t => actors.Contains(t.Id)).ToArray(),
                    value =>
                    {
                        o.TargetKind = "Roster";
                        o.TargetIds.Clear();
                        if (value.Length > 0)
                            o.TargetIds.Add(value);
                    }
                );
                if (actors.Count == 0)
                    Message(group, "Add a roster containing exactly one bot to select an individual actor.");
            }
            else
            {
                Choice(
                    group,
                    "Target group",
                    o.TargetKind,
                    Names(o.Type == MissionObjective.Survive ? new[] { "Encounter" } : new[] { "Encounter", "Roster", "Squad" }),
                    value =>
                    {
                        o.TargetKind = value;
                        o.TargetIds.Clear();
                    }
                );
                foreach (var target in MissionAuthoring.Targets(layout, o.TargetKind))
                    Toggle(
                        group,
                        target.Name,
                        o.TargetIds.Contains(target.Id),
                        value =>
                        {
                            o.TargetIds.Remove(target.Id);
                            if (value)
                                o.TargetIds.Add(target.Id);
                        }
                    );
            }
            if (o.Type == MissionObjective.Defend)
            {
                Choice(
                    group,
                    "Area",
                    o.ZoneId,
                    MissionAuthoring.SourceTargets(mission, layout, MissionSignals.Sample),
                    value => o.ZoneId = value
                );
                Number(group, "Uncontested seconds", o.Seconds, value => o.Seconds = value);
            }
            if (o.Type == MissionObjective.Protect)
                Choice(
                    group,
                    "Protect until",
                    o.UntilEventId,
                    new[] { ("", "Mission exit") }
                        .AsValueEnumerable()
                        .Concat(mission.Events.AsValueEnumerable().Select(e => (e.Id, e.Name)))
                        .ToArray(),
                    value => o.UntilEventId = value
                );
            if (o.Required)
                foreach (var checkpoint in layout.Checkpoints)
                    Toggle(
                        group,
                        "Require before " + checkpoint.Name,
                        mission.Requirements.AsValueEnumerable().Any(r => r.CheckpointId == checkpoint.Id && r.ObjectiveIds.Contains(o.Id)),
                        value =>
                        {
                            var gate = mission.Requirements.AsValueEnumerable().FirstOrDefault(r => r.CheckpointId == checkpoint.Id);
                            if (gate == null)
                            {
                                gate = new() { CheckpointId = checkpoint.Id };
                                mission.Requirements.Add(gate);
                            }
                            gate.ObjectiveIds.Remove(o.Id);
                            if (value)
                                gate.ObjectiveIds.Add(o.Id);
                        }
                    );
            Button(
                group,
                "Duplicate objective",
                () =>
                {
                    var copy = SeasonCompiler.Copy(o);
                    copy.Id = MissionAuthoring.NewId();
                    copy.Name += " copy";
                    mission.Objectives.Add(copy);
                }
            );
            Button(group, "Remove objective", () => mission.Objectives.Remove(o));
        }
        foreach (var rule in mission.Events)
        {
            var group = _document.Clone<Foldout>("InspectorSection");
            group.text = rule.Name;
            group.value = true;
            scroll.Add(group);
            Text(group, "Name", rule.Name, value => rule.Name = value);
            Choice(
                group,
                "When",
                rule.Source,
                Names(MissionAuthoring.Sources),
                value =>
                {
                    rule.Source = value;
                    rule.SourceId = "";
                }
            );
            if (rule.Source != MissionSignals.Start)
                Choice(
                    group,
                    "Source",
                    rule.SourceId,
                    MissionAuthoring.SourceTargets(mission, layout, rule.Source),
                    value => rule.SourceId = value
                );
            foreach (var action in rule.Actions)
            {
                Choice(
                    group,
                    "Then",
                    action.Type,
                    Names(new[] { MissionAction.Encounter, MissionAction.Objective, MissionAction.Timer }),
                    value =>
                    {
                        action.Type = value;
                        action.TargetId = "";
                    }
                );
                if (action.Type == MissionAction.Timer)
                {
                    Text(group, "Timer name", action.TargetId, value => action.TargetId = value);
                    Number(group, "Seconds", action.Seconds, value => action.Seconds = value);
                }
                else
                    Choice(
                        group,
                        "Target",
                        action.TargetId,
                        MissionAuthoring.ActionTargets(mission, layout, action.Type),
                        value => action.TargetId = value
                    );
                Button(group, "Remove action", () => rule.Actions.Remove(action));
            }
            Button(group, "Add action", () => rule.Actions.Add(new()));
            Button(
                group,
                "Duplicate event",
                () =>
                {
                    var copy = SeasonCompiler.Copy(rule);
                    copy.Id = MissionAuthoring.NewId();
                    copy.Name += " copy";
                    mission.Events.Add(copy);
                }
            );
            Button(group, "Remove event", () => mission.Events.Remove(rule));
        }
    }

    private static (string, string)[] Names(IEnumerable<string> values) =>
        values.AsValueEnumerable().Select(v => (v, MissionAuthoring.Label(v))).ToArray();

    private void Message(VisualElement parent, string text)
    {
        var label = _document.Clone<Label>("FieldMessage");
        label.text = text;
        parent.Add(label);
    }

    private void Button(VisualElement parent, string title, Action action)
    {
        var button = _document.Clone<Button>("Action");
        button.text = title;
        button.clicked += () => Change(action);
        parent.Add(button);
    }

    private void Text(VisualElement parent, string title, string value, Action<string> update)
    {
        var field = _document.Clone<TextField>("Field");
        field.label = title;
        field.value = value;
        field.isDelayed = true;
        field.maxLength = 120;
        field.RegisterValueChangedCallback(e => Change(() => update(e.newValue)));
        parent.Add(field);
    }

    private void Number(VisualElement parent, string title, double value, Action<double> update)
    {
        var field = new DoubleField(title) { value = value, isDelayed = true };
        field.RegisterValueChangedCallback(e => Change(() => update(e.newValue)));
        parent.Add(field);
    }

    private void Toggle(VisualElement parent, string title, bool value, Action<bool> update)
    {
        var field = new Toggle(title) { value = value };
        field.RegisterValueChangedCallback(e => Change(() => update(e.newValue)));
        parent.Add(field);
    }

    private void Choice(
        VisualElement parent,
        string title,
        string value,
        IEnumerable<(string Id, string Name)> choices,
        Action<string> update,
        bool edit = true
    )
    {
        var entries = choices.AsValueEnumerable().ToList();
        if (!entries.AsValueEnumerable().Any(e => e.Id == value))
            entries.Insert(0, (value, value.Length == 0 ? "Choose…" : "Missing selection"));
        var index = entries.FindIndex(e => e.Id == value);
        var field = new DropdownField(title, entries.AsValueEnumerable().Select(e => e.Name).ToList(), index);
        field.RegisterValueChangedCallback(_ =>
        {
            var id = entries[field.index].Id;
            if (edit)
                Change(() => update(id));
            else
                update(id);
        });
        parent.Add(field);
    }
}
