using UnityEngine;
using UnityEngine.UIElements;
using ZLinq;

namespace WTT.Campaigns.Client.Authoring.Views;

internal sealed partial class RaidEditorView
{
    private readonly List<(Foldout Fold, string Id, bool Default, VisualElement[] Sources)> _sections = new();
    private readonly List<(VisualElement Header, VisualElement Owner)> _pinned = new();
    private string _inspectorContext = "";
    private Button? _sceneMore;
    private VisualElement[] _secondarySceneGroups = Array.Empty<VisualElement>();
    private string _noticeText = "",
        _lastNotice = "";
    private bool _noticeExpanded;
    private string _conflictFingerprint = "";
    private Label _cameraError = null!;

    private void BuildUsability()
    {
        _cameraError = Document.Clone<Label>("FieldMessage");
        _cameraError.AddToClassList("editor-toolbar-validation");
        Element("Workspace").Add(_cameraError);
        foreach (
            var (owner, ids) in new[]
            {
                ("SceneInspector", new[] { "SceneHeadingGroup", "SceneFocusGroup", "ScenePlaceGroup", "SceneEditGroup" }),
                ("RecordInspector", new[] { "NameGroup", "RecordActionsGroup" }),
                ("MapInspector", new[] { "MapNameGroup", "MapRecordActions" }),
            }
        )
        {
            var header = Document.Clone<VisualElement>("InspectorHeader");
            var scroll = Element("PropertyScroll");
            scroll.parent.Insert(scroll.parent.IndexOf(scroll), header);
            foreach (var id in ids)
                header.Add(Element(id));
            _pinned.Add((header, Element(owner)));
        }
        Element("DetailsGroup").Insert(0, Element("IdentityGroup"));
        Action fitInspector = () => Element("Inspector").EnableInClassList("editor-narrow", Element("Inspector").layout.width < 320);
        Element("Inspector").RegisterCallback<GeometryChangedEvent>(_ => AfterLayout(fitInspector));
        Section("Preview", false, "ScenePreviewGroup", "ScenePreviewRetryGroup");
        Section("Scene details", false, "SceneRestoreGroup", "SceneInfoGroup");
        Section("Transform", true, "PositionGroup", "RotationGroup", "SizeGroup", "RadiusGroup", "PlacementGroup");
        Section("Zone settings", true, "ZoneUsesGroup", "ZoneScopeGroup", "HazardInfoGroup", "SniperSoundGroup");
        Section("Trigger", true, "AiTriggerSection");
        Section("Wave", true, "AiWaveSection");
        Section("Roster", true, "AiRosterSection");
        Section("Assignments", false, "AiAssignmentSection");
        Section("Patrol", true, "AiPatrolSection");
        Section("Map transform", true, "MapPositionGroup", "MapRotationGroup", "MapSizeGroup", "MapShapeGroup", "MapPlacementGroup");
        Section("Door settings", true, "DoorInspectorGroup");

        foreach (var tool in ToolIds)
        {
            var controls = _toolControls[tool];
            var filters = controls["Library"].Element.Q<VisualElement>("BrowserFilters");
            filters.Insert(0, controls["Search"].Element);
            Action fitLibrary = () =>
                controls["Library"].Element.EnableInClassList("editor-narrow", controls["Library"].Element.layout.width < 380);
            controls["Library"].Element.RegisterCallback<GeometryChangedEvent>(_ => AfterLayout(fitLibrary));
        }
        var scene = _toolControls["Scene"];
        _secondarySceneGroups = new[] { scene["Library"].Element.Q("ScenePropActions"), scene["Library"].Element.Q("SceneWorldActions") };
        _sceneMore = Document.Clone<Button>("Action");
        _sceneMore.text = "More actions…";
        _sceneMore.tooltip = "Move, copy, hide, barriers and doors";
        scene["CreationTools"].Element.Add(_sceneMore);
        scene["Library"].Element.RegisterCallback<GeometryChangedEvent>(_ => AfterLayout(RefreshSceneOverflow));
        scene["Library"].Element.schedule.Execute(RefreshSceneOverflow);
        _sceneMore.clicked += () =>
        {
            if (!Activate("Scene"))
                return;
            var buttons = new[] { "MapMoveObject", "MapCopyObject", "MapHideObject", "MapBarrier", "MapDoor" }
                .AsValueEnumerable()
                .Select(id => (EditorButton)scene[id])
                .Where(b => b.Visible)
                .ToArray();
            ShowChoices(
                _sceneMore,
                buttons.AsValueEnumerable().Select(b => b.text).ToArray(),
                -1,
                i => buttons[i].interactable,
                i => buttons[i].onClick.Invoke()
            );
        };
        Button(
            "NoticeToggle",
            () =>
            {
                _noticeExpanded = !_noticeExpanded;
                RefreshNotice();
            }
        );
        Button(
            "NoticeDismiss",
            () =>
            {
                _noticeText = "";
                _noticeExpanded = false;
                RefreshNotice();
            }
        );
        RefreshNotice();
    }

    private void Section(string title, bool expanded, params string[] ids)
    {
        var sources = ids.AsValueEnumerable().Select(Element).ToArray();
        var first = sources[0];
        var fold = Document.Clone<Foldout>("InspectorSection");
        fold.text = title;
        first.parent.Insert(first.parent.IndexOf(first), fold);
        foreach (var source in sources)
            fold.Add(source);
        fold.SetValueWithoutNotify(expanded);
        fold.RegisterValueChangedCallback(evt =>
        {
            if (evt.target == fold)
                EditorLayoutPreferences.SetExpanded(_inspectorContext, title, evt.newValue);
        });
        _sections.Add((fold, title, expanded, sources));
    }

    internal void InspectorContext(string tool, string kind)
    {
        var context = tool + "/" + kind;
        if (_inspectorContext == context)
            return;
        _inspectorContext = context;
        Windows.DetailContext(context);
        foreach (var section in _sections)
            section.Fold.SetValueWithoutNotify(EditorLayoutPreferences.Expanded(context, section.Id, section.Default));
    }

    private void RefreshUsability()
    {
        _positionChoice?.Invoke();
        var speed = Get<EditorInput>("CameraSpeed");
        _cameraError.style.display = speed.Invalid ? DisplayStyle.Flex : DisplayStyle.None;
        if (speed.Invalid)
        {
            _cameraError.text = speed.ValidationMessage;
            var point = Element("Workspace").WorldToLocal(speed.Element.worldBound.min);
            _cameraError.style.left = Math.Clamp(point.x, 8, Math.Max(8, Document.Width - 268));
            _cameraError.style.top = point.y + speed.Element.worldBound.height + 2;
        }
        foreach (var (header, owner) in _pinned)
            header.style.display = owner.style.display.value != DisplayStyle.None ? DisplayStyle.Flex : DisplayStyle.None;
        foreach (var section in _sections)
            section.Fold.style.display = section.Sources.AsValueEnumerable().Any(s => s.style.display.value != DisplayStyle.None)
                ? DisplayStyle.Flex
                : DisplayStyle.None;
    }

    private void RefreshSceneOverflow()
    {
        if (_sceneMore != null)
        {
            var scene = _toolControls["Scene"];
            var width = scene["Library"].Element.layout.width;
            var desired = 110f;
            foreach (var group in _secondarySceneGroups)
            foreach (var button in group.Query<Button>().ToList())
                if (button.style.display.value != DisplayStyle.None)
                    desired +=
                        button
                            .MeasureTextSize(button.text, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined)
                            .x + 32;
            var overflow = width < desired + 110;
            foreach (var group in _secondarySceneGroups)
                group.style.display = overflow ? DisplayStyle.None : DisplayStyle.Flex;
            _sceneMore.style.display = overflow ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    internal void Feedback(string synchronization, string preview, string notice)
    {
        Text("Status", synchronization);
        Text("PreviewStatus", preview);
        Visible("PreviewStatus", preview.Length > 0);
        Windows.SetTooltip("Status", synchronization);
        Windows.SetTooltip("PreviewStatus", preview);
        if (notice != _lastNotice)
        {
            _lastNotice = notice;
            if (notice.Length > 0 && notice != preview)
                _noticeText = notice;
        }
        RefreshNotice();
    }

    private void RefreshNotice()
    {
        Visible("NoticeToggle", _noticeText.Length > 0);
        Caption("NoticeToggle", _noticeExpanded ? "Hide notice" : "Notice…");
        Element("NoticeToggle").tooltip = _noticeText;
        Text("NoticeText", _noticeText);
        Visible("NoticePanel", _noticeExpanded && _noticeText.Length > 0);
    }

    private void PresentConflicts(WTT.Campaigns.Shared.Authoring.AuthoringResponse conflict)
    {
        var fingerprint = Newtonsoft.Json.JsonConvert.SerializeObject(conflict.Conflicts);
        if (_conflictFingerprint == fingerprint)
            return;
        _conflictFingerprint = fingerprint;
        var list = (ScrollView)Element("ConflictRows");
        list.Clear();
        Text("ConflictPath", conflict.Conflicts.Count + " conflicting fields. Each choice below resolves the entire conflict set.");
        foreach (var entry in conflict.Conflicts)
        {
            var row = Document.Clone<VisualElement>("ConflictRow");
            foreach (var label in row.Query<Label>().ToList())
                label.enableRichText = false;
            row.Q<Label>("Path").text = entry.Path;
            var diff = EditorInteractionPolicy.Difference(entry.Local ?? "", entry.Remote ?? "");
            foreach (var side in new[] { "Local", "Remote" })
            {
                row.Q<Label>(side + "Before").text = diff.Before;
                row.Q<Label>(side + "Changed").text = (side == "Local" ? diff.Local : diff.Remote) is { Length: > 0 } changed
                    ? changed
                    : "(empty)";
                row.Q<Label>(side + "After").text = diff.After;
                row.Q<Label>(side + "Before").style.display = diff.Before.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                row.Q<Label>(side + "After").style.display = diff.After.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }
            list.Add(row);
        }
    }
}
