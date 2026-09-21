using Cysharp.Threading.Tasks;
using EFT.Interactive;
using Newtonsoft.Json;
using SPT.Common.Http;
using UnityEngine;
using WTT.Campaigns.Client.Authoring.Console;
using WTT.Campaigns.Client.Authoring.Scenes;
using WTT.Campaigns.Client.Authoring.Views;
using WTT.Campaigns.Shared.Authoring;
using WTT.Campaigns.Shared.Spatial;
using ZLinq;

namespace WTT.Campaigns.Client.Authoring.Controllers;

internal sealed partial class EditorCatalogController
{
    private string _sceneRebindId = "";

    private readonly SceneBrowserState _sceneBrowser = new();

    private string _sceneTab
    {
        get => _sceneBrowser.Tab;
        set => _sceneBrowser.Tab = value;
    }

    private string _sceneFilter
    {
        get => _sceneBrowser.Filter;
        set => _sceneBrowser.Filter = value;
    }

    private string _catalogSelection = "";
    private readonly SceneCatalogRequestState _catalogRequests = new();

    private readonly Dictionary<string, Transform> _sceneRoots = new();

    private readonly HashSet<string> _unsupportedSceneIds = new();

    private readonly Dictionary<string, SceneCatalogEntry> _localCatalogEntries = new();

    private readonly HashSet<Transform> _discoveredRoots = new();

    private readonly Dictionary<string, string> _propShapes = new();

    private readonly Dictionary<string, string> _sceneSourcePaths = new();
    private List<SceneCatalogEntry> _localMatches = new();
    private string _localMatchesKey = "";
    private bool _sceneDirty;
    private int _sceneRevision;
    private float _nextScenePublish;
    internal int SceneRevision
    {
        get
        {
            if (_sceneDirty && Time.realtimeSinceStartup >= _nextScenePublish)
            {
                _sceneDirty = false;
                _nextScenePublish = Time.realtimeSinceStartup + .25f;
                _sceneRevision++;
            }
            return _sceneRevision;
        }
    }

    private int _pendingCatalogEntries;
    private int _catalogViewRevision;
    private string _levelQueryKey = "";
    private int _levelQueryGeneration;
    private bool _levelQueryLoading;
    private SceneCatalogEntry[] _levelMatches = Array.Empty<SceneCatalogEntry>();
    private CancellationTokenSource _levelSearchLifetime = new();

    private async Task FindLevelEntries(string search, bool hideUnavailable, int generation)
    {
        try
        {
            var found = await NativeLevelPropLibrary.Search(search, hideUnavailable, _levelSearchLifetime.Token);
            if (_disposed || generation != _levelQueryGeneration)
                return;
            _levelMatches = found;
        }
        catch (Exception error)
        {
            if (!_disposed && generation == _levelQueryGeneration)
                _context.ReportFeedback(error.Message, ConsoleSeverity.Error);
        }
        finally
        {
            if (!_disposed && generation == _levelQueryGeneration)
            {
                _levelQueryLoading = false;
                _catalogViewRevision++;
                _context.LibraryKey = "";
            }
        }
    }

    private string _catalogSource = "All game";

    private bool _containerTemplatesRequested;

    private HashSet<string>? _containerTemplates;

    private bool AssetCatalog =>
        SceneWorkspace
        && _sceneTab == "Catalog"
        && (_sceneFilter == "Containers" || (_sceneFilter == "Props" && _catalogSource == "All game"));

    private SceneCatalogResponse? _catalog;

    private SceneCatalogEntry? _selectedCatalogEntry;

    private readonly Dictionary<string, (string Id, SceneCatalogEntry? Entry)> _filterSelections = new();

    private sealed class ThumbnailImage
    {
        internal Texture Texture = null!;
        internal Rect Uv;
        internal bool Owned;
    }

    private readonly ScenePreviewCache<ThumbnailImage> _previews = new(
        128,
        image =>
        {
            if (image.Owned && image.Texture)
                SceneThumbnailRenderer.Release(image.Texture);
        }
    );

    private readonly List<ThumbnailJob> _thumbnailQueue = new();

    private bool _thumbnailWorker;
    private SceneThumbnailSchedule _thumbnailSchedule = new();
    private SceneThumbnailRenderer? _thumbnailRenderer;
    private int _thumbnailRenderFrame = -1;

    private CancellationTokenSource _thumbnailLifetime = new();

    internal bool SceneWorkspace => _context.ToolId == "Scene" && EditorMode.Ready;

    internal bool RemoteCatalog => SceneWorkspace && _sceneTab == "Catalog" && _sceneFilter is "Loot" or "Presets";

    private int _catalogPageSize = 10;

    internal int LibraryPageSize => SceneWorkspace && _sceneTab == "Catalog" ? _catalogPageSize : 10;

    internal bool PagedCatalog => RemoteCatalog || AssetCatalog;

    internal int LibraryOffset => PagedCatalog ? 0 : _context.Page * LibraryPageSize;

    internal int LibraryTotal => PagedCatalog ? _catalog?.Total ?? 0 : _context.Rows.Count;

    internal void DiscoverSceneNode(Transform node)
    {
        if (node.GetComponent<Door>() is { } nativeDoor && nativeDoor.GetType() == typeof(Door))
        {
            _sceneRoots[node.GetInstanceID().ToString()] = node;
            _sceneDirty = true;
            return;
        }
        if (
            !EditorMode.Ready
            || (!node.GetComponent<MeshRenderer>() && !node.GetComponent<LootItem>() && !node.GetComponent<LootableContainer>())
        )
            return;
        // Native interactables own their identity even inside an indexed visual root.
        if (!node.GetComponent<LootItem>() && !node.GetComponent<LootableContainer>())
            for (var parent = node; parent; parent = parent.parent)
                if (_discoveredRoots.Contains(parent))
                    return;
        if (node.GetComponentInParent<EFT.Player>() || node.GetComponentInParent<Canvas>())
            return;
        var root = MapSceneAdapter.Root(node) ?? WTT.Campaigns.UI.Controls.SceneSelectionGeometry.VisualRoot(node);
        if (!root || _sceneRoots.ContainsKey(root!.GetInstanceID().ToString()))
            return;
        _sceneRoots[root!.GetInstanceID().ToString()] = root;
        _sceneDirty = true;
        if (MapSceneAdapter.Supported(root).Length > 0)
        {
            _unsupportedSceneIds.Add(root.GetInstanceID().ToString());
            return;
        }
        _discoveredRoots.Add(root);
        if (root.GetComponent<LootItem>() || root.GetComponent<LootableContainer>())
            return;
        if (MapSceneAdapter.Supported(root, copy: true).Length > 0)
        {
            _unsupportedSceneIds.Add(root.GetInstanceID().ToString());
            return;
        }
        // Asset identities are session-local; saved copies retain their concrete source binding.
        var signature =
            root.GetComponentsInChildren<MeshFilter>(true)
                .AsValueEnumerable()
                .Where(m => m.sharedMesh)
                .Select(m =>
                    m.sharedMesh.GetInstanceID()
                    + ":"
                    + m.transform.localPosition
                    + ":"
                    + m.transform.localRotation
                    + ":"
                    + m.transform.localScale
                )
                .JoinToString("|")
            + root.GetComponentsInChildren<MeshRenderer>(true)
                .AsValueEnumerable()
                .SelectMany(r => r.sharedMaterials)
                .Select(m => m ? m.GetInstanceID().ToString() : "0")
                .JoinToString("|");
        using var hash = System.Security.Cryptography.SHA256.Create();
        var key = BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(signature)));
        if (!_propShapes.ContainsKey(key))
            _propShapes.Add(key, root.GetInstanceID().ToString());
    }

    internal void Bind(RaidEditorView view)
    {
        BindContainerControls(view);
        _context.BindDoorControls(view);
        void Button(string name, Action action) =>
            view.Button(
                name,
                () =>
                {
                    try
                    {
                        _context.CancelDrag();
                        action();
                        _context.Refresh();
                    }
                    catch (Exception e)
                    {
                        _context.ReportFeedback(e.Message, ConsoleSeverity.Error);
                        Plugin.Error(e);
                    }
                }
            );
        foreach (var tab in new[] { "Catalog", "Existing", "Changes" })
        {
            var value = tab;
            Button(
                "Scene" + tab,
                () =>
                {
                    CancelPlacement();
                    _sceneTab = value;
                    if (value == "Catalog")
                    {
                        _context.Picked = null;
                        _context.SelectionId = "";
                        _context.SceneSelectionPose = null;
                        _context.SceneSelectionError = "";
                    }
                    _context.Page = 0;
                }
            );
        }
        Button("ScenePreviewRetry", RetryThumbnail);
        Button(
            "SceneHideUnavailable",
            () =>
            {
                _sceneBrowser.HideUnavailable = !_sceneBrowser.HideUnavailable;
                _catalogViewRevision++;
                _context.Page = 0;
                _catalogRequests.Reset();
                _context.LibraryKey = "";
                if (_sceneBrowser.HideUnavailable && SelectedCatalogUnavailable())
                    ClearCatalogSelection();
            }
        );
        // These actions must not cancel an active transform or placement.
        view.Button("SceneFrame", _context.FrameSceneSelection);
        view.Button(
            "SceneAnchor",
            () =>
            {
                if (_context.IsDragging || _placementRequests.Active || _context.Walking)
                    return;
                _context.CenterAnchor = !_context.CenterAnchor;
                _context.Refresh();
            }
        );
        view.Dropdown(
            "SceneSource",
            index =>
            {
                CancelPlacement();
                _filterSelections[_catalogSource + ":" + _sceneFilter] = (_catalogSelection, _selectedCatalogEntry);
                _catalogSource = index == 0 ? "All game" : "Current map";
                var saved = _filterSelections.GetValueOrDefault(_catalogSource + ":" + _sceneFilter);
                _catalogSelection = saved.Id ?? "";
                _selectedCatalogEntry = saved.Entry;
                _context.Page = 0;
                _catalogRequests.Reset();
                _context.LibraryKey = "";
                _context.Refresh();
            }
        );
        view.Dropdown(
            "SceneFilter",
            index =>
            {
                var filters = SceneBrowserState.Filters.AsValueEnumerable().Where(f => SceneBrowserState.Available(_sceneTab, f)).ToArray();
                if (index < 0 || index >= filters.Length)
                    return;
                _context.CancelDrag();
                CancelPlacement();
                if (_sceneTab == "Catalog")
                    _filterSelections[_catalogSource + ":" + _sceneFilter] = (_catalogSelection, _selectedCatalogEntry);
                _sceneFilter = filters[index];
                if (_sceneTab == "Catalog")
                {
                    var saved = _filterSelections.GetValueOrDefault(_catalogSource + ":" + _sceneFilter);
                    _catalogSelection = saved.Id ?? "";
                    _selectedCatalogEntry = saved.Entry;
                }
                _context.Page = 0;
                _context.LibraryKey = "";
                _context.Refresh();
            }
        );
        Button(
            "ScenePlace",
            () =>
            {
                _ = BeginPlacement();
            }
        );
        Button("SceneRepeat", () => _repeatPlacement = !_repeatPlacement);
        view.BindCatalogActivation(id =>
        {
            if (!SceneWorkspace || _sceneTab != "Catalog")
                return;
            _context.SelectRow(id);
            _ = BeginPlacement();
        });
        Button("SceneMove", () => SceneTransform("Move"));
        Button("SceneRotate", () => SceneTransform("Rotate"));
        Button("SceneScale", () => SceneTransform("Scale"));
        Button("SceneRemove", RemoveSceneObject);
        Button(
            "SceneRestore",
            () =>
            {
                _context.DeleteMapRecord();
                _context.SelectionId = "";
            }
        );
        Button(
            "SceneRebind",
            () =>
            {
                if (!CanSceneEdit || _context.MapPoint is not MapObjectEdit)
                    return;
                _sceneRebindId = _context.SelectionId;
                _context.Picking = true;
                _context.ReportFeedback("Click the replacement object · Esc cancels");
            }
        );
    }

    private bool SelectedCatalogUnavailable() =>
        _selectedCatalogEntry != null
            ? CatalogError(_selectedCatalogEntry).Length > 0
            : _sceneFilter == "Doors"
                && _catalogSelection.Length > 0
                && (
                    !_sceneRoots.TryGetValue(_catalogSelection, out var source)
                    || !source
                    || SceneDoorPlacement.Restriction(source).Length > 0
                );

    private void ClearCatalogSelection()
    {
        CancelPlacement();
        _catalogSelection = "";
        _selectedCatalogEntry = null;
    }

    private bool CatalogPending(SceneCatalogEntry entry) =>
        entry.Error.Length == 0
        && entry.AssetTarget?.Bundle != NativeContainerLibrary.BundleKey
        && entry.AssetTarget?.Kind is "AssetContainer" or "Container"
        && _containerTemplates == null;

    private static bool CatalogMatches(SceneCatalogEntry entry, SceneSearchQuery search) =>
        search.Matches(entry.Name, entry.Id, entry.AssetTarget?.Path, entry.AssetTarget?.Bundle, entry.AssetTarget?.Asset);

    private bool SceneMatches(string id, string label, SceneSearchQuery search)
    {
        if (search.Matches(label, id))
            return true;
        if (!_sceneRoots.TryGetValue(id, out var source) || !source)
            return false;
        if (!_sceneSourcePaths.TryGetValue(id, out var path))
            _sceneSourcePaths[id] = path = source.gameObject.scene.name + ":/" + MapSceneAdapter.PathOf(source);
        return search.Matches(label, id, path);
    }

    internal void SceneRows(string search)
    {
        search = search.Trim();
        var query = new SceneSearchQuery(search);
        _pendingCatalogEntries = 0;
        if (_sceneTab != "Catalog")
        {
            void Add(string id, string label, string kind)
            {
                if (_sceneBrowser.Matches(kind))
                    _context.Rows.Add((id, label));
            }
            if (_sceneTab == "Existing")
                foreach (var pair in _sceneRoots)
                {
                    var target = pair.Value;
                    if (!target || !target.gameObject.activeInHierarchy || _context.MapScene?.RecordAt(target) != null)
                        continue;
                    var kind =
                        target.GetComponent<Door>() ? "Doors"
                        : target.GetComponent<LootableContainer>() ? "Containers"
                        : target.GetComponent<LootItem>() ? "Loot"
                        : "Props";
                    Add(pair.Key, target.name, kind);
                }
            if (_context.Layout != null)
            {
                foreach (var item in _context.Layout.Objects)
                    if (_sceneTab == "Changes" || item.Operation == "Copy")
                        Add(
                            item.Id,
                            (item.Operation == "Hide" ? "Removed" : item.Operation) + " · " + item.Name,
                            item.Target.Kind is "Container" or "AssetContainer" ? "Containers"
                                : item.Target.Kind == "Loot" ? "Loot"
                                : "Props"
                        );
                foreach (var item in _context.Layout.Loot)
                    Add(item.Id, "Placed loot · " + item.Name, "Loot");
                foreach (var item in _context.Layout.Barriers)
                    Add(item.Id, "Barrier · " + item.Name, "Barriers");
                foreach (var item in _context.Layout.Doors)
                    if (_sceneTab == "Changes" || item.PlaceNew)
                        Add(item.Id, "Door · " + item.Name + " · " + item.State, "Doors");
            }
        }
        else if (_sceneFilter == "Doors")
        {
            foreach (var pair in _sceneRoots)
                if (pair.Value && pair.Value.GetComponent<Door>() && _context.MapScene?.RecordAt(pair.Value) == null)
                {
                    var error = SceneDoorPlacement.Restriction(pair.Value);
                    if (_sceneBrowser.ShowCatalogEntry(error))
                        _context.Rows.Add((pair.Key, pair.Value.name + " · Door" + (error.Length > 0 ? " · Unavailable: " + error : "")));
                }
            if (_sceneBrowser.HideUnavailable && SelectedCatalogUnavailable())
                ClearCatalogSelection();
        }
        else if (AssetCatalog)
        {
            var levels = _sceneFilter == "Props" && _catalogSource == "All game";
            var levelKey = search + "\n" + _sceneBrowser.HideUnavailable;
            if (levels && _levelQueryKey != levelKey)
            {
                _levelSearchLifetime.Cancel();
                _levelSearchLifetime.Dispose();
                _levelSearchLifetime = new();
                _levelQueryKey = levelKey;
                _levelMatches = Array.Empty<SceneCatalogEntry>();
                _levelQueryLoading = true;
                _ = FindLevelEntries(search, _sceneBrowser.HideUnavailable, ++_levelQueryGeneration);
            }
            if (!_containerTemplatesRequested)
            {
                _containerTemplatesRequested = true;
                _ = LoadContainerTemplates();
            }
            var localKey =
                $"{search}|{_sceneFilter}|{_catalogSource}|{_sceneBrowser.HideUnavailable}|{SceneRevision}|{_containerTemplates?.Count}";
            if (_localMatchesKey != localKey)
            {
                var entries = new List<SceneCatalogEntry>();
                var nativeEntries = NativeContainerLibrary.Entries().AsValueEnumerable().ToArray();
                var nativeTemplates = new HashSet<string>(nativeEntries.AsValueEnumerable().Select(e => e.AssetTarget!.Template).ToArray());
                foreach (var entry in nativeEntries)
                {
                    if ((_sceneFilter == "Containers") != (entry.AssetTarget?.Kind == "AssetContainer"))
                        continue;
                    if (
                        _catalogSource == "Current map"
                        && !_sceneRoots
                            .Values.AsValueEnumerable()
                            .Any(t => t && t.GetComponent<LootableContainer>()?.Template == entry.AssetTarget?.Template)
                    )
                        continue;
                    if (!CatalogMatches(entry, query))
                        continue;
                    entries.Add(entry);
                }
                var sourceIds =
                    _sceneFilter == "Containers"
                        ? _sceneRoots.Keys.AsValueEnumerable().ToArray()
                        : _propShapes.Values.AsValueEnumerable().Concat(_unsupportedSceneIds.AsValueEnumerable()).Distinct().ToArray();
                foreach (var id in sourceIds)
                {
                    if (!_sceneRoots.TryGetValue(id, out var source) || !source)
                        continue;
                    var container = source.GetComponent<LootableContainer>();
                    if (container && nativeTemplates.Contains(container.Template))
                        continue;
                    if ((_sceneFilter == "Containers") != (container != null))
                        continue;
                    if (_localCatalogEntries.TryGetValue(id, out var localEntry))
                    {
                        entries.Add(localEntry);
                        continue;
                    }
                    try
                    {
                        var binding = (_context.MapScene ??= new()).CaptureOriginal(source);
                        entries.Add(
                            new SceneCatalogEntry
                            {
                                Id = "scene:" + id,
                                Name = source.name + " � Current map",
                                AssetTarget = binding,
                                Error = container
                                    ? MapSceneAdapter.ContainerCopyRestriction(source)
                                    : MapSceneAdapter.Supported(source, copy: true),
                            }
                        );
                    }
                    catch (Exception e)
                    {
                        entries.Add(
                            new SceneCatalogEntry
                            {
                                Id = "scene:" + id,
                                Name = source.name,
                                Error = e.Message,
                            }
                        );
                    }
                    _localCatalogEntries[id] = entries[entries.Count - 1];
                }
                var ids = new HashSet<string>(StringComparer.Ordinal);
                entries.RemoveAll(e => !ids.Add(e.Id) || !CatalogMatches(e, query));
                _pendingCatalogEntries = entries.AsValueEnumerable().Count(e => CatalogPending(e));
                entries.RemoveAll(e => !_sceneBrowser.ShowCatalogEntry(CatalogError(e)));
                entries.Sort(query.Compare);
                _localMatches = entries;
                _localMatchesKey = localKey;
            }
            var levelEntries = levels ? _levelMatches : Array.Empty<SceneCatalogEntry>();
            var total = _localMatches.Count + levelEntries.Length;
            _context.Page = Math.Min(_context.Page, Math.Max(0, (total - 1) / LibraryPageSize));
            var page = SceneCatalogPage.Merge(
                _localMatches,
                levelEntries,
                _context.Page * LibraryPageSize,
                LibraryPageSize,
                query.Compare
            );
            if (
                _sceneBrowser.HideUnavailable
                && !_levelQueryLoading
                && _catalogSelection.Length > 0
                && _selectedCatalogEntry != null
                && !SceneCatalogPage.Contains(_localMatches, _selectedCatalogEntry, query.Compare)
                && !SceneCatalogPage.Contains(levelEntries, _selectedCatalogEntry, query.Compare)
            )
                ClearCatalogSelection();
            _catalog = new SceneCatalogResponse { Entries = page, Total = total };
            if (_catalogSelection.Length > 0)
                _selectedCatalogEntry = page.AsValueEnumerable().FirstOrDefault(e => e.Id == _catalogSelection) ?? _selectedCatalogEntry;
            foreach (var entry in page)
                _context.Rows.Add((entry.Id, entry.Name + (CatalogError(entry).Length > 0 ? " � Unavailable" : "")));
            return;
        }
        else if (RemoteCatalog)
        {
            var key =
                _catalogSource
                + ":"
                + _sceneFilter
                + ":"
                + search
                + ":"
                + _context.Page
                + ":"
                + LibraryPageSize
                + ":"
                + _sceneBrowser.HideUnavailable;
            if (_catalogRequests.Key != key)
            {
                _catalog = null;
                _ = FetchCatalog(key, search, _context.Page, LibraryPageSize, _catalogRequests.Begin(key));
            }
            if (_catalog != null)
                foreach (var entry in _catalog.Entries)
                    _context.Rows.Add((entry.Id, entry.Name));
            return;
        }
        else
        {
            var ids = _propShapes.Values.AsValueEnumerable().ToArray();
            foreach (var id in ids)
                if (_sceneRoots.TryGetValue(id, out var t) && t)
                    _context.Rows.Add(
                        (
                            id,
                            t.name
                                + (
                                    t.GetComponent<LootItem>() ? " · Loot"
                                    : t.GetComponent<LootableContainer>() ? " · Container"
                                    : ""
                                )
                        )
                    );
        }
        if (_sceneFilter != "Doors")
            _context.Rows.RemoveAll(r => !SceneMatches(r.Id, r.Label, query));
    }

    private async Task FetchCatalog(string key, string search, int page, int pageSize, int generation)
    {
        try
        {
            var result = new SceneCatalogResponse();
            var category = _sceneFilter == "Presets" ? "Presets" : "Items";
            var hideUnavailable = _sceneBrowser.HideUnavailable;
            var sessionId = EditorMode.SessionId;
            // The installed server serves ten records per request. Assemble the
            // visible range without changing that contract or publishing partial pages.
            foreach (var request in CatalogGridLayout.Requests(page, pageSize))
            {
                var batch = JsonConvert.DeserializeObject<SceneCatalogResponse>(
                    await RequestHandler.PostJsonAsync(
                        "/wtt-campaigns/editor/catalogue",
                        JsonConvert.SerializeObject(
                            new SceneCatalogRequest
                            {
                                SessionId = sessionId,
                                Search = search,
                                Page = request.Page,
                                Category = category,
                                HideUnavailable = hideUnavailable,
                                TemplateIds =
                                    _catalogSource == "Current map"
                                        ? _sceneRoots
                                            .Values.AsValueEnumerable()
                                            .Where(t => t && t.GetComponent<LootItem>())
                                            .Select(t => t.GetComponent<LootItem>().TemplateId)
                                            .Where(id => WTT.Campaigns.Shared.Seasons.SeasonValidator.IsId(id))
                                            .Distinct()
                                            .ToList()
                                        : null,
                            }
                        )
                    )
                );
                if (!_catalogRequests.IsCurrent(generation, key))
                    return;
                if (batch == null || batch.Error != null)
                    throw new InvalidOperationException(batch?.Error ?? "The catalog did not respond.");
                result.Total = batch.Total;
                for (var i = request.Skip; i < Math.Min(batch.Entries.Count, request.Skip + request.Take); i++)
                    result.Entries.Add(batch.Entries[i]);
                if ((request.Page + 1) * 10 >= batch.Total)
                    break;
            }
            _catalog = result;
            if (_catalog.Error != null)
                _context.ReportFeedback(_catalog.Error, ConsoleSeverity.Error);
        }
        catch (Exception e)
        {
            if (_catalogRequests.IsCurrent(generation, key))
            {
                _context.ReportFeedback(e.Message, ConsoleSeverity.Error);
                _catalog = new SceneCatalogResponse { Error = e.Message };
            }
        }
        finally
        {
            if (_catalogRequests.IsCurrent(generation, key))
            {
                _catalogRequests.Finish(generation, key);
                _context.Refresh(false);
            }
        }
    }

    internal void SelectSceneRow(string id)
    {
        if (_sceneTab == "Catalog")
        {
            _context.Picked = null;
            _context.SceneSelectionPose = null;
            _context.SceneSelectionError = "";
            _catalogSelection = id;
            _selectedCatalogEntry = _catalog?.Entries.AsValueEnumerable().FirstOrDefault(e => e.Id == id);
            _context.SelectionId = "";
            CancelPlacement();
        }
        else if (_sceneRoots.TryGetValue(id, out var target) && target)
            SelectSceneTarget(target);
        else
        {
            _context.SelectionId = id;
            _context.Picked = null;
            _context.SceneSelectionPose = null;
            _context.SceneSelectionError = "";
            if (_context.TransformTool == "Scale" && !_context.CanTransformScene("Scale"))
                _context.TransformTool = "Move";
        }
    }

    internal void SelectSceneTarget(Transform hit)
    {
        ClearCatalogSelection();
        var authored = _context.MapScene?.RecordAt(hit);
        if (_sceneRebindId.Length > 0 && authored != null)
        {
            _context.ReportFeedback("Choose an original map object as the replacement.", ConsoleSeverity.Warning);
            return;
        }
        if (authored != null)
        {
            _context.SceneSelectionPose = null;
            _context.SceneSelectionError = "";
            _context.SelectionId = authored;
            _context.Picked = null;
            if (_context.TransformTool == "Scale" && !_context.CanTransformScene("Scale"))
                _context.TransformTool = "Move";
            _context.Refresh();
            _context.View?.Windows.ShowPanel(ConfiguredContainer != null ? "LootConfiguration" : "Inspector", true);
            return;
        }
        var nativeDoor = hit.GetComponentInParent<Door>();
        if (nativeDoor && nativeDoor.GetType() == typeof(Door))
        {
            _context.Picked = nativeDoor.transform;
            _context.SceneSelectionPose = null;
            _context.SceneSelectionError = "";
            var doorBinding = MapSceneAdapter.Capture(_context.Picked, true);
            _context.SelectionId =
                _context
                    .Layout?.Doors.AsValueEnumerable()
                    .FirstOrDefault(d => !d.PlaceNew && d.Target.Path == doorBinding.Path && d.Target.Scene == doorBinding.Scene)
                    ?.Id
                ?? "";
            _context.Refresh();
            _context.View?.Windows.ShowPanel("Inspector", true);
            return;
        }
        var root = MapSceneAdapter.Root(hit) ?? WTT.Campaigns.UI.Controls.SceneSelectionGeometry.VisualRoot(hit);
        if (!root)
        {
            _context.ReportFeedback(MapSceneAdapter.Supported(hit), ConsoleSeverity.Warning);
            return;
        }
        var error = MapSceneAdapter.Supported(root);
        if (_sceneRebindId.Length > 0 && error.Length > 0)
        {
            _context.ReportFeedback(error, ConsoleSeverity.Warning);
            return;
        }
        if (_sceneRebindId.Length > 0)
        {
            var id = _sceneRebindId;
            var target = (_context.MapScene ??= new()).CaptureOriginal(root!);
            var rebound = _context.Layout?.Objects.AsValueEnumerable().FirstOrDefault(o => o.Id == id);
            if (rebound == null)
            {
                _sceneRebindId = "";
                return;
            }
            if (rebound.Target.Kind != target.Kind)
            {
                _context.ReportFeedback("Select the same object type as the original target.", ConsoleSeverity.Warning);
                return;
            }
            _context.MapEdit(l => l.Objects.AsValueEnumerable().Single(o => o.Id == id).Target = target);
            _sceneRebindId = "";
            _context.SceneSelectionError = "";
            _context.SceneSelectionPose = null;
            _context.SelectionId = id;
            _context.Picked = root;
            _context.Refresh();
            return;
        }
        _context.Picked = root;
        _context.SelectionId = "";
        var binding = error.Length == 0 ? (_context.MapScene ??= new()).CaptureOriginal(root!) : null;
        var record = _context
            .Layout?.Objects.AsValueEnumerable()
            .FirstOrDefault(o =>
                binding != null
                && o.Operation != "Copy"
                && o.Target.Kind == binding.Kind
                && o.Target.Path == binding.Path
                && o.Target.Scene == binding.Scene
            );
        if (record != null)
            _context.SelectionId = record.Id;
        _context.SetSceneSelectionPose(root!, binding, error);
        _context.ReportFeedback(
            error.Length > 0 ? "Selected for inspection. " + error : "Selected " + root!.name,
            error.Length > 0 ? ConsoleSeverity.Warning : ConsoleSeverity.Info
        );
        _context.Refresh();
        _context.View?.Windows.ShowPanel(ConfiguredContainer != null ? "LootConfiguration" : "Inspector", true);
    }

    internal void SceneTransform(string tool)
    {
        if (!CanSceneEdit)
            return;
        if (!_context.CanTransformScene(tool))
            return;
        if (_context.MapPoint is MapObjectEdit { Operation: "Hide" })
            return;
        _context.TransformTool = tool;
    }

    internal bool CanSceneEdit =>
        EditorMode.Ready
        && !_context.Walking
        && !_context.WalkAssetsLoading
        && _context.Layout != null
        && _context.Session?.Definition != null
        && _context.Session.Conflict == null
        && !_context.Session.Retired;

    private void RemoveSceneObject()
    {
        if (_context.MapDoor != null)
        {
            _context.DeleteMapRecord();
            _context.Picked = null;
            _context.SceneSelectionPose = null;
            return;
        }
        if (!CanSceneEdit)
            return;
        if (_context.MapPoint is MapLootPlacement or MapObjectEdit { Operation: "Copy" } or MapVolume)
            _context.DeleteMapRecord();
        else if (_context.MapPoint is MapObjectEdit edit)
            _context.MapEdit(_ => edit.Operation = "Hide");
        else if (_context.Picked)
            _context.CaptureMapObject("Hide");
    }

    private async Task LoadContainerTemplates()
    {
        try
        {
            var sessionId = EditorMode.SessionId;
            var response = JsonConvert.DeserializeObject<SceneContainerResponse>(
                await RequestHandler.PostJsonAsync(
                    "/wtt-campaigns/editor/containers",
                    JsonConvert.SerializeObject(new SceneContainerRequest { SessionId = EditorMode.SessionId })
                )
            );
            if (sessionId != EditorMode.SessionId || !_containerTemplatesRequested)
                return;
            if (response == null || response.Error != null)
                throw new InvalidOperationException(response?.Error ?? "No container response.");
            _containerTemplates = new HashSet<string>(response.Templates);
            _catalogViewRevision++;
            _context.LibraryKey = "";
        }
        catch (Exception e)
        {
            _context.ReportFeedback(e.Message, ConsoleSeverity.Error);
        }
    }

    internal void Reset()
    {
        if (_disposed)
            return;
        ClearContainerControls();
        _containerTemplatesRequested = false;
        _containerTemplates = null;
        _context.ClearSceneSelection();
        _context.LibraryKey = "";
        CancelPlacement();
        _sceneRebindId = "";
        _catalogRequests.Reset();
        _catalog = null;
        _catalogSelection = "";
        _selectedCatalogEntry = null;
        _filterSelections.Clear();
        _sceneBrowser.Reset();
        _repeatPlacement = false;
        _unsupportedSceneIds.Clear();
        _localCatalogEntries.Clear();
        _localMatches.Clear();
        _localMatchesKey = "";
        _sceneDirty = false;
        _sceneRevision++;
        _nextScenePublish = 0;
        _levelQueryKey = "";
        _levelSearchLifetime.Cancel();
        _levelSearchLifetime.Dispose();
        _levelSearchLifetime = new();
        _levelQueryGeneration++;
        _levelQueryLoading = false;
        _levelMatches = Array.Empty<SceneCatalogEntry>();
        _sceneRoots.Clear();
        _sceneSourcePaths.Clear();
        _discoveredRoots.Clear();
        _propShapes.Clear();
        _thumbnailLifetime.Cancel();
        _thumbnailLifetime.Dispose();
        _thumbnailLifetime = new();
        _previews.Clear();
        _thumbnailSchedule.Want(Array.Empty<string>());
        _thumbnailSchedule = new();
        _thumbnailRenderer?.Dispose();
        _thumbnailRenderer = null;
        _thumbnailWorker = false;
        _thumbnailQueue.Clear();
    }
}
