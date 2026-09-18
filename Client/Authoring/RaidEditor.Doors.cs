using EFT.Interactive;
using WTT.Campaigns.Client.Authoring.Scenes;
using WTT.Campaigns.Client.Authoring.Views;
using WTT.Campaigns.Shared.Spatial;
using ZLinq;

namespace WTT.Campaigns.Client.Authoring;

public sealed partial class RaidEditor
{
    private readonly List<WTT.Campaigns.Shared.Authoring.SceneCatalogEntry> _doorKeys = new();
    private int _doorKeyIndex,
        _doorKeyGeneration;
    private Door? PickedDoor => _picked ? _picked!.GetComponent<Door>() : null;

    private void EditDoor(Action<MapDoorEdit> change)
    {
        if (!CanSceneEdit || MapDoor == null && !PickedDoor)
            return;
        MapEdit(layout =>
        {
            var edit = layout.Doors.AsValueEnumerable().FirstOrDefault(d => d.Id == _selected);
            if (edit == null)
            {
                var target = MapSceneAdapter.Capture(PickedDoor!.transform, true);
                edit = layout
                    .Doors.AsValueEnumerable()
                    .FirstOrDefault(d => !d.PlaceNew && d.Target.Path == target.Path && d.Target.Scene == target.Scene);
                if (edit == null)
                {
                    edit = new MapDoorEdit
                    {
                        Id = MapId(),
                        Name = PickedDoor.name,
                        Target = target,
                    };
                    layout.Doors.Add(edit);
                }
            }
            change(edit);
            _selected = edit.Id;
        });
    }

    private void BindDoorControls(RaidEditorView view)
    {
        view.Dropdown("DoorStartState", i => EditDoor(d => d.State = new[] { "Unchanged", "Shut", "Open", "Locked" }[i]));
        view.Input("DoorKeyId", text => EditDoor(d => d.KeyId = text.Trim()));
        view.Input("DoorKeySearch", text => _ = SearchDoorKeys(text));
        view.Dropdown("DoorKeyResults", i => _doorKeyIndex = i);
        view.Button(
            "DoorUseKey",
            () =>
            {
                if (_doorKeyIndex >= 0 && _doorKeyIndex < _doorKeys.Count && _doorKeys[_doorKeyIndex].KeyId is { Length: > 0 } key)
                    EditDoor(d => d.KeyId = key);
            }
        );
        view.Button("DoorOriginalKey", () => EditDoor(d => d.KeyId = null));
        view.Dropdown("DoorBreach", i => EditDoor(d => d.CanBeBreached = i == 0 ? null : i == 1));
        view.Dropdown("DoorOperatable", i => EditDoor(d => d.Operatable = i == 0 ? null : i == 1));
    }

    private void PresentDoorControls()
    {
        var visible = SceneWorkspace && _sceneTab != "Catalog" && (MapDoor != null || PickedDoor);
        var view = _view!;
        view.Visible("DoorInspectorGroup", visible);
        if (!visible)
            return;
        view.Visible("MapInspector", true);
        var door = MapDoor;
        view.Value("MapName", door?.Name ?? PickedDoor!.name);
        view.Get<EditorInput>("MapName").readOnly = !CanSceneEdit;
        view.Visible("MapPositionGroup", door?.PlaceNew == true);
        view.Visible("MapRotationGroup", door?.PlaceNew == true);
        view.Visible("MapSizeGroup", false);
        view.Visible("MapAtPlayer", door?.PlaceNew == true);
        view.Get<EditorButton>("SceneRemove").interactable = CanSceneEdit && door != null;
        view.SetDropdown(
            "DoorStartState",
            new List<EditorChoice.OptionData> { new("Original"), new("Closed"), new("Open"), new("Locked") },
            Array.IndexOf(new[] { "Unchanged", "Shut", "Open", "Locked" }, door?.State ?? "Unchanged")
        );
        view.Value("DoorKeyId", door?.KeyId ?? (PickedDoor ? PickedDoor!.KeyId : ""));
        view.SetDropdown(
            "DoorBreach",
            new List<EditorChoice.OptionData> { new("Original"), new("Allowed"), new("Disabled") },
            door?.CanBeBreached is { } breach
                ? breach
                    ? 1
                    : 2
                : 0
        );
        view.SetDropdown(
            "DoorOperatable",
            new List<EditorChoice.OptionData> { new("Original"), new("Enabled"), new("Disabled") },
            door?.Operatable is { } operate
                ? operate
                    ? 1
                    : 2
                : 0
        );
        foreach (var id in new[] { "DoorStartState", "DoorBreach", "DoorOperatable" })
            view.Get<EditorChoice>(id).interactable = CanSceneEdit;
        view.Get<EditorInput>("DoorKeyId").readOnly = !CanSceneEdit;
        view.Get<EditorButton>("DoorOriginalKey").interactable = CanSceneEdit;
        view.SetDropdown(
            "DoorKeyResults",
            _doorKeys.AsValueEnumerable().Select(k => new EditorChoice.OptionData(k.Name)).ToList(),
            _doorKeyIndex
        );
        view.Get<EditorButton>("DoorUseKey").interactable = CanSceneEdit && _doorKeys.Count > 0;
        view.Text(
            "DoorHelp",
            "Starting state applies once. Find a key by name and choose Use selected key, or enter its template ID. Empty removes the key requirement; Original key preserves the source setting. New doors retain their original size."
        );
    }

    private async Task SearchDoorKeys(string search)
    {
        var generation = ++_doorKeyGeneration;
        var session = EditorMode.SessionId;
        try
        {
            var response = Newtonsoft.Json.JsonConvert.DeserializeObject<WTT.Campaigns.Shared.Authoring.SceneCatalogResponse>(
                await SPT.Common.Http.RequestHandler.PostJsonAsync(
                    "/wtt-campaigns/editor/catalogue",
                    Newtonsoft.Json.JsonConvert.SerializeObject(
                        new WTT.Campaigns.Shared.Authoring.SceneCatalogRequest
                        {
                            SessionId = session,
                            Category = "Keys",
                            Search = search,
                        }
                    )
                )
            );
            if (generation != _doorKeyGeneration || session != EditorMode.SessionId)
                return;
            _doorKeys.Clear();
            if (response?.Error != null)
                throw new InvalidOperationException(response.Error);
            if (response != null)
                _doorKeys.AddRange(response.Entries.AsValueEnumerable().Where(e => !string.IsNullOrEmpty(e.KeyId)).ToArray());
            _doorKeyIndex = 0;
            Refresh();
        }
        catch (Exception error)
        {
            if (generation == _doorKeyGeneration && session == EditorMode.SessionId)
            {
                _notice = error.Message;
                Refresh();
            }
        }
    }
}
