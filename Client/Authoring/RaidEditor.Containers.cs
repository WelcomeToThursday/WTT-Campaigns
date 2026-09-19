using Newtonsoft.Json;
using SPT.Common.Http;
using WTT.Campaigns.Client.Authoring.Scenes;
using WTT.Campaigns.Client.Authoring.Views;
using WTT.Campaigns.Shared.Authoring;
using WTT.Campaigns.Shared.Spatial;
using ZLinq;

namespace WTT.Campaigns.Client.Authoring;

public sealed partial class RaidEditor
{
    private readonly List<SceneCatalogEntry> _containerItems = new();
    private readonly List<SceneCatalogEntry> _containerKeys = new();
    private int _containerKeyIndex,
        _containerKeySearchGeneration;
    private string _containerSelectionId = "";
    private readonly List<string> _containerPools = new();
    private int _containerItemIndex,
        _containerContentIndex,
        _containerQuantity = 1,
        _containerSearchGeneration;
    private readonly Dictionary<string, string> _containerItemNames = new();
    private MapObjectEdit? ConfiguredContainer =>
        SceneWorkspace && _sceneTab != "Catalog" && MapPoint is MapObjectEdit edit && SceneAssetRules.IsContainer(edit) ? edit : null;

    private void EditContainer(Action<ContainerSettings> change)
    {
        if (!CanSceneEdit || ConfiguredContainer is not { } selected)
            return;
        try
        {
            MapEdit(layout =>
            {
                var edit = layout.Objects.AsValueEnumerable().First(o => o.Id == selected.Id);
                change(edit.Container ??= new ContainerSettings());
            });
        }
        catch (Exception e)
        {
            _notice = e.Message;
            Plugin.Error(e);
        }
        Refresh();
    }

    private void BindContainerControls(RaidEditorView view)
    {
        view.Dropdown("ContainerMode", i => EditContainer(s => s.Mode = new[] { "Native", "Fixed", "Empty" }[i]));
        view.Dropdown(
            "ContainerPool",
            i =>
            {
                if (i < _containerPools.Count)
                    EditContainer(s => s.LootPool = _containerPools[i]);
            }
        );
        view.Input(
            "ContainerChance",
            value =>
            {
                if (int.TryParse(value, out var chance) && chance is >= 0 and <= 100)
                    EditContainer(s => s.SpawnChance = chance);
                else
                {
                    _notice = "Spawn chance must be a whole number from 0 to 100.";
                    Refresh();
                }
            }
        );
        view.Button(
            "ContainerLock",
            () =>
            {
                if (
                    ConfiguredContainer?.Container is not { Locked: true }
                    && string.IsNullOrEmpty(ConfiguredContainer?.Container?.KeyTemplate)
                )
                {
                    _notice = "Find a key in Access and choose Use selected key first.";
                    Refresh();
                    return;
                }
                EditContainer(s => s.Locked = !s.Locked);
            }
        );
        view.Input("ContainerKeySearch", value => _ = SearchContainerItems(value, true));
        view.Dropdown("ContainerKeyItem", i => _containerKeyIndex = i);
        view.Input("ContainerSearch", value => _ = SearchContainerItems(value));
        view.Dropdown("ContainerItem", i => _containerItemIndex = i);
        view.Input(
            "ContainerQuantity",
            value =>
            {
                if (int.TryParse(value, out var count) && count is > 0 and <= 10000)
                    _containerQuantity = count;
                else
                {
                    _notice = "Quantity must be between 1 and 10000.";
                    Refresh();
                }
            }
        );
        view.Dropdown("ContainerContents", i => _containerContentIndex = i);
        view.Button(
            "ContainerAdd",
            () =>
            {
                if (_containerItemIndex >= _containerItems.Count)
                    return;
                var item = _containerItems[_containerItemIndex];
                EditContainer(s =>
                {
                    if (s.Contents.Count >= 100)
                        throw new InvalidOperationException("The container already has 100 fixed entries.");
                    s.Mode = "Fixed";
                    s.Contents.Add(new ContainerContent { Template = item.Id, Count = _containerQuantity });
                });
            }
        );
        view.Button(
            "ContainerUseKey",
            () =>
            {
                if (_containerKeyIndex >= 0 && _containerKeyIndex < _containerKeys.Count)
                    _ = UseContainerKey(_containerKeys[_containerKeyIndex].Id);
            }
        );
        view.Button(
            "ContainerRemove",
            () =>
                EditContainer(s =>
                {
                    if (_containerContentIndex < s.Contents.Count)
                        s.Contents.RemoveAt(_containerContentIndex);
                })
        );
    }

    private async Task UseContainerKey(string template)
    {
        var selected = ConfiguredContainer?.Id;
        var session = EditorMode.SessionId;
        try
        {
            var response = JsonConvert.DeserializeObject<SceneCatalogResponse>(
                await RequestHandler.PostJsonAsync(
                    "/wtt-campaigns/editor/catalogue",
                    JsonConvert.SerializeObject(
                        new SceneCatalogRequest
                        {
                            SessionId = session,
                            Category = "Keys",
                            Id = template,
                        }
                    )
                )
            );
            if (session != EditorMode.SessionId || selected != ConfiguredContainer?.Id)
                return;
            if (response?.Error != null || response?.Total != 1)
                throw new InvalidOperationException(response?.Error ?? "Choose a key or keycard to lock this container.");
            EditContainer(s => s.KeyTemplate = template);
        }
        catch (Exception e)
        {
            _notice = e.Message;
            Refresh();
        }
    }

    private string ContainerItemName(string template) =>
        Plugin.Localized(template + " Name", _containerItemNames.GetValueOrDefault(template, template));

    private void ClearContainerControls()
    {
        _containerSearchGeneration++;
        _containerKeySearchGeneration++;
        _containerKeys.Clear();
        _containerKeyIndex = 0;
        _containerSelectionId = "";
        _containerItems.Clear();
        _containerPools.Clear();
        _containerItemNames.Clear();
        _containerItemIndex = _containerContentIndex = 0;
        _containerQuantity = 1;
    }

    private async Task SearchContainerItems(string search, bool keys = false)
    {
        var generation = keys ? ++_containerKeySearchGeneration : ++_containerSearchGeneration;
        var session = EditorMode.SessionId;
        try
        {
            var response = JsonConvert.DeserializeObject<SceneCatalogResponse>(
                await RequestHandler.PostJsonAsync(
                    "/wtt-campaigns/editor/catalogue",
                    JsonConvert.SerializeObject(
                        new SceneCatalogRequest
                        {
                            SessionId = session,
                            Search = search,
                            Category = keys ? "Keys" : "Items",
                            Page = 0,
                        }
                    )
                )
            );
            if (generation != (keys ? _containerKeySearchGeneration : _containerSearchGeneration) || session != EditorMode.SessionId)
                return;
            if (response == null || response.Error != null)
                throw new InvalidOperationException(response?.Error ?? "Item search failed.");
            var results = keys ? _containerKeys : _containerItems;
            results.Clear();
            results.AddRange(response.Entries);
            foreach (var entry in response.Entries)
                _containerItemNames[entry.Id] = entry.Name;
            if (keys)
                _containerKeyIndex = 0;
            else
                _containerItemIndex = 0;
            _notice = response.Total > response.Entries.Count ? "Showing the first 10 matches. Refine the search to find your item." : "";
            Refresh();
        }
        catch (Exception e)
        {
            if (generation == (keys ? _containerKeySearchGeneration : _containerSearchGeneration))
            {
                _notice = e.Message;
                Refresh();
            }
        }
    }

    private void PresentContainerControls()
    {
        var view = _view!;
        var edit = ConfiguredContainer;
        view.Visible("ContainerSettingsGroup", edit != null);
        view.Visible("ContainerEmpty", edit == null);
        view.Text("ContainerSelection", edit?.Name ?? "Select a lootable container");
        if (edit == null)
        {
            if (_containerSelectionId.Length > 0)
                ClearContainerControls();
            return;
        }
        if (_containerSelectionId != edit.Id)
        {
            ClearContainerControls();
            _containerSelectionId = edit.Id;
            view.Value("ContainerSearch", "");
            view.Value("ContainerKeySearch", "");
            view.Windows.ShowPanel("LootConfiguration", true);
            ((UnityEngine.UIElements.ScrollView)view.Element("ContainerScroll")).scrollOffset = UnityEngine.Vector2.zero;
        }
        var settings = edit.Container ?? new ContainerSettings();
        var mode =
            settings.Mode == "Fixed" ? 1
            : settings.Mode == "Empty" ? 2
            : 0;
        view.SetDropdown("ContainerMode", new() { new("Random native loot"), new("Fixed contents"), new("Empty") }, mode);
        var names = new Dictionary<string, string>();
        foreach (var entry in NativeContainerLibrary.Entries())
            names[entry.AssetTarget!.Template] = entry.Name;
        _containerPools.Clear();
        _containerPools.Add("");
        if (_containerTemplates != null)
            _containerPools.AddRange(_containerTemplates.AsValueEnumerable().OrderBy(id => names.GetValueOrDefault(id, id)).ToArray());
        view.SetDropdown(
            "ContainerPool",
            _containerPools
                .AsValueEnumerable()
                .Select(id => new EditorChoice.OptionData(id == "" ? "This container's native pool" : names.GetValueOrDefault(id, id)))
                .ToList(),
            Math.Max(0, _containerPools.IndexOf(settings.LootPool))
        );
        view.Visible("ContainerPoolGroup", mode == 0);
        view.Value("ContainerChance", settings.SpawnChance.ToString());
        view.Checked("ContainerLock", settings.Locked);
        view.Text("ContainerKey", settings.KeyTemplate.Length == 0 ? "No key selected" : "Key: " + ContainerItemName(settings.KeyTemplate));
        view.SetDropdown(
            "ContainerItem",
            _containerItems.AsValueEnumerable().Select(e => new EditorChoice.OptionData(e.Name)).ToList(),
            _containerItemIndex
        );
        view.Value("ContainerQuantity", _containerQuantity.ToString());
        view.SetDropdown(
            "ContainerContents",
            settings
                .Contents.AsValueEnumerable()
                .Select(c => new EditorChoice.OptionData(ContainerItemName(c.Template) + " × " + c.Count))
                .ToList(),
            _containerContentIndex
        );
        view.Visible("ContainerFixedSection", mode == 1);
        view.SetDropdown(
            "ContainerKeyItem",
            _containerKeys.AsValueEnumerable().Select(e => new EditorChoice.OptionData(e.Name)).ToList(),
            _containerKeyIndex
        );
        view.Visible("ContainerRemoveGroup", mode == 1);
        view.Element("ContainerAdd").SetEnabled(_containerItems.Count > 0);
        view.Element("ContainerUseKey").SetEnabled(_containerKeys.Count > 0);
        view.Element("ContainerRemove").SetEnabled(settings.Contents.Count > 0);
        if (_containerItems.Count == 0)
            view.SetDropdown("ContainerItem", new() { new("Search for an item above") }, 0);
        if (_containerKeys.Count == 0)
            view.SetDropdown("ContainerKeyItem", new() { new("Search for a key above") }, 0);
        if (settings.Contents.Count == 0)
            view.SetDropdown("ContainerContents", new() { new("No fixed items added") }, 0);
        view.Element("ContainerItem").SetEnabled(_containerItems.Count > 0);
        view.Element("ContainerKeyItem").SetEnabled(_containerKeys.Count > 0);
        view.Element("ContainerContents").SetEnabled(settings.Contents.Count > 0);
    }
}
