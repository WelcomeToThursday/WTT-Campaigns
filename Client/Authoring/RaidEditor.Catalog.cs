using System.Threading;
using Cysharp.Threading.Tasks;
using EFT.Interactive;
using EFT.UI.DragAndDrop;
using Newtonsoft.Json;
using SPT.Common.Http;
using UnityEngine;
using UnityEngine.EventSystems;
using WTT.Campaigns.Client.Authoring.Scenes;
using WTT.Campaigns.Client.Authoring.Views;
using WTT.Campaigns.Client.Spatial;
using WTT.Campaigns.Shared.Authoring;
using WTT.Campaigns.Shared.Spatial;
using ZLinq;
using Button = WTT.Campaigns.Client.Authoring.Views.EditorButton;
using Text = WTT.Campaigns.Client.Authoring.Views.EditorLabel;

namespace WTT.Campaigns.Client.Authoring;

public sealed partial class RaidEditor
{
    private string _sceneRebindId = "";
    private string _sceneTab = "Catalog",
        _sceneFilter = "Props",
        _catalogKey = "",
        _catalogSelection = "";
    private readonly Dictionary<string, Transform> _sceneRoots = new();
    private readonly HashSet<string> _unsupportedSceneIds = new();
    private readonly Dictionary<string, SceneCatalogEntry> _localCatalogEntries = new();
    private readonly HashSet<Transform> _discoveredRoots = new();
    private readonly Dictionary<string, string> _propShapes = new();
    private string _catalogSource = "All game";
    private SceneAssetCatalog? _assetCatalog;
    private SceneAssetCatalog.Model? _placementAsset;
    private HashSet<string>? _containerTemplates;
    private bool AssetCatalog =>
        SceneWorkspace
        && _sceneTab == "Catalog"
        && (_sceneFilter == "Containers" || (_sceneFilter == "Props" && _catalogSource == "All game"));
    private SceneCatalogResponse? _catalog;
    private SceneCatalogEntry? _selectedCatalogEntry;
    private readonly Dictionary<string, (string Id, SceneCatalogEntry? Entry)> _filterSelections = new();
    private bool _catalogLoading;
    private int _catalogGeneration;
    private GameObject? _placement;
    private MapTarget? _placementProp;
    private SceneCatalogEntry? _placementLoot;
    private CancellationTokenSource? _placementLifetime;
    private bool _placementPooled;

    private sealed class ThumbnailImage
    {
        internal Texture Texture = null!;
        internal Rect Uv;
        internal bool Owned;
    }

    private readonly ScenePreviewCache<ThumbnailImage> _previews = new(
        64,
        image =>
        {
            if (image.Owned && image.Texture)
                Destroy(image.Texture);
        }
    );
    private readonly List<ThumbnailJob> _thumbnailQueue = new();
    private bool _thumbnailWorker;
    private CancellationTokenSource _thumbnailLifetime = new();
    private bool SceneWorkspace => _mode == "Scene" && EditorMode.Ready;
    private bool RemoteCatalog => SceneWorkspace && _sceneTab == "Catalog" && _sceneFilter is "Loot" or "Presets";
    private int _catalogPageSize = 10;
    private int LibraryPageSize => SceneWorkspace && _sceneTab == "Catalog" ? _catalogPageSize : 10;
    private int LibraryOffset => RemoteCatalog ? 0 : _page * LibraryPageSize;
    private int LibraryTotal => RemoteCatalog ? _catalog?.Total ?? 0 : _rows.Count;

    private void DiscoverSceneNode(Transform node)
    {
        if (
            !EditorMode.Ready
            || (!node.GetComponent<MeshRenderer>() && !node.GetComponent<LootItem>() && !node.GetComponent<LootableContainer>())
        )
            return;
        for (var parent = node; parent; parent = parent.parent)
            if (_discoveredRoots.Contains(parent))
                return;
        if (node.GetComponentInParent<EFT.Player>() || node.GetComponentInParent<Canvas>())
            return;
        var root = MapSceneAdapter.Root(node) ?? WTT.Campaigns.UI.Controls.SceneSelectionGeometry.VisualRoot(node);
        if (!root || _sceneRoots.ContainsKey(root!.GetInstanceID().ToString()))
            return;
        _sceneRoots[root!.GetInstanceID().ToString()] = root;
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

    private void BindSceneControls(RaidEditorView view)
    {
        void Button(string name, Action action) =>
            view.Button(
                name,
                () =>
                {
                    try
                    {
                        CancelDrag();
                        action();
                        Refresh();
                    }
                    catch (Exception e)
                    {
                        _notice = e.Message;
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
                    _page = 0;
                }
            );
        }
        Button("ScenePreviewRetry", RetryThumbnail);
        // These actions must not cancel an active transform or placement.
        view.Button("SceneFrame", FrameSceneSelection);
        view.Button(
            "SceneAnchor",
            () =>
            {
                if (_drag != null || _placementLifetime != null || _walking)
                    return;
                _centerAnchor = !_centerAnchor;
                Refresh();
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
                _page = 0;
                _catalogKey = _libraryKey = "";
                Refresh();
            }
        );
        foreach (var filter in new[] { "Props", "Containers", "Loot", "Presets" })
        {
            var value = filter;
            Button(
                "Scene" + filter,
                () =>
                {
                    CancelPlacement();
                    _filterSelections[_catalogSource + ":" + _sceneFilter] = (_catalogSelection, _selectedCatalogEntry);
                    _sceneFilter = value;
                    var saved = _filterSelections.GetValueOrDefault(_catalogSource + ":" + value);
                    _catalogSelection = saved.Id ?? "";
                    _selectedCatalogEntry = saved.Entry;
                    _page = 0;
                }
            );
        }
        Button(
            "ScenePlace",
            () =>
            {
                _ = BeginPlacement();
            }
        );
        Button("SceneMove", () => SceneTransform("Move"));
        Button("SceneRotate", () => SceneTransform("Rotate"));
        Button("SceneScale", () => SceneTransform("Scale"));
        Button("SceneRemove", RemoveSceneObject);
        Button(
            "SceneRestore",
            () =>
            {
                DeleteMapRecord();
                _selected = "";
            }
        );
        Button(
            "SceneRebind",
            () =>
            {
                if (!CanSceneEdit || MapPoint is not MapObjectEdit)
                    return;
                _sceneRebindId = _selected;
                _picking = true;
                _notice = "Click the replacement object · Esc cancels";
            }
        );
    }

    private void SceneRows(string search)
    {
        if (_sceneTab == "Changes")
        {
            if (Layout != null)
            {
                foreach (var item in Layout.Objects)
                    _rows.Add((item.Id, (item.Operation == "Hide" ? "Removed" : item.Operation) + " · " + item.Name));
                foreach (var item in Layout.Loot)
                    _rows.Add((item.Id, "Placed loot · " + item.Name));
                foreach (var item in Layout.Barriers)
                    _rows.Add((item.Id, "Barrier · " + item.Name));
                foreach (var item in Layout.Doors)
                    _rows.Add((item.Id, "Door · " + item.Name + " · " + item.State));
            }
        }
        else if (AssetCatalog)
        {
            if (_assetCatalog == null)
            {
                _assetCatalog = new SceneAssetCatalog();
                _ = LoadContainerTemplates();
                _assetCatalog.Start(
                    () =>
                    {
                        _libraryKey = "";
                    },
                    () => AssetCatalog && _open
                );
            }
            var entries = new List<SceneCatalogEntry>();
            foreach (var entry in _assetCatalog.Entries)
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
                if (
                    entry.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0
                    && entry.AssetTarget!.Bundle.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0
                )
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
                if ((_sceneFilter == "Containers") != (container != null))
                    continue;
                if (source.name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                if (_localCatalogEntries.TryGetValue(id, out var localEntry))
                {
                    entries.Add(localEntry);
                    continue;
                }
                try
                {
                    var binding = (_mapScene ??= new()).CaptureOriginal(source);
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
            entries.Sort(
                (a, b) =>
                {
                    var order = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                    return order != 0 ? order : string.CompareOrdinal(a.Id, b.Id);
                }
            );
            _catalog = new SceneCatalogResponse { Entries = entries, Total = entries.Count };
            if (_catalogSelection.Length > 0)
                _selectedCatalogEntry = entries.AsValueEnumerable().FirstOrDefault(e => e.Id == _catalogSelection) ?? _selectedCatalogEntry;
            foreach (var entry in entries)
                _rows.Add((entry.Id, entry.Name + (CatalogError(entry).Length > 0 ? " � Unavailable" : "")));
            return;
        }
        else if (RemoteCatalog)
        {
            var key = _catalogSource + ":" + _sceneFilter + ":" + search + ":" + _page + ":" + LibraryPageSize;
            if (_catalogKey != key)
            {
                _catalogKey = key;
                _catalog = null;
                _ = FetchCatalog(key, search, _page, LibraryPageSize, ++_catalogGeneration);
            }
            if (_catalog != null)
                foreach (var entry in _catalog.Entries)
                    _rows.Add((entry.Id, entry.Name));
            return;
        }
        else
        {
            var ids =
                _sceneTab == "Catalog" ? _propShapes.Values.AsValueEnumerable().ToArray() : _sceneRoots.Keys.AsValueEnumerable().ToArray();
            foreach (var id in ids)
                if (_sceneRoots.TryGetValue(id, out var t) && t && (_sceneTab == "Catalog" || t.gameObject.activeInHierarchy))
                    _rows.Add(
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
            if (_sceneTab == "Existing" && Layout != null)
            {
                foreach (var item in Layout.Objects.AsValueEnumerable().Where(o => o.Operation == "Copy"))
                    _rows.Add((item.Id, item.Name));
                foreach (var item in Layout.Loot)
                    _rows.Add((item.Id, item.Name));
            }
        }
        _rows.RemoveAll(r => r.Label.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0);
    }

    private async Task FetchCatalog(string key, string search, int page, int pageSize, int generation)
    {
        _catalogLoading = true;
        try
        {
            var result = new SceneCatalogResponse();
            var category = _sceneFilter == "Presets" ? "Presets" : "Items";
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
                if (generation != _catalogGeneration || _catalogKey != key)
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
                _notice = _catalog.Error;
        }
        catch (Exception e)
        {
            if (generation == _catalogGeneration)
            {
                _notice = e.Message;
                _catalog = new SceneCatalogResponse { Error = e.Message };
            }
        }
        finally
        {
            if (generation == _catalogGeneration)
            {
                _catalogLoading = false;
                Refresh(false);
            }
        }
    }

    private void SelectSceneRow(string id)
    {
        if (_sceneTab == "Catalog")
        {
            _catalogSelection = id;
            _selectedCatalogEntry = _catalog?.Entries.AsValueEnumerable().FirstOrDefault(e => e.Id == id);
            _selected = "";
            _ = BeginPlacement();
        }
        else if (_sceneRoots.TryGetValue(id, out var target) && target)
            SelectSceneTarget(target);
        else
        {
            _selected = id;
            _picked = null;
            _sceneSelectionPose = null;
            _sceneSelectionError = "";
            if (_tool == "Scale" && !CanTransformScene("Scale"))
                _tool = "Move";
        }
    }

    private void SelectSceneTarget(Transform hit)
    {
        var authored = _mapScene?.RecordAt(hit);
        if (_sceneRebindId.Length > 0 && authored != null)
        {
            _notice = "Choose an original map object as the replacement.";
            return;
        }
        if (authored != null)
        {
            _sceneSelectionPose = null;
            _sceneSelectionError = "";
            _selected = authored;
            _picked = null;
            _sceneTab = "Existing";
            if (_tool == "Scale" && !CanTransformScene("Scale"))
                _tool = "Move";
            Refresh();
            _view?.Windows.ShowPanel("Inspector", true);
            return;
        }
        var root = MapSceneAdapter.Root(hit) ?? WTT.Campaigns.UI.Controls.SceneSelectionGeometry.VisualRoot(hit);
        if (!root)
        {
            _notice = MapSceneAdapter.Supported(hit);
            return;
        }
        var error = MapSceneAdapter.Supported(root);
        if (_sceneRebindId.Length > 0 && error.Length > 0)
        {
            _notice = error;
            return;
        }
        if (_sceneRebindId.Length > 0)
        {
            var id = _sceneRebindId;
            var target = (_mapScene ??= new()).CaptureOriginal(root!);
            var rebound = Layout?.Objects.AsValueEnumerable().FirstOrDefault(o => o.Id == id);
            if (rebound == null)
            {
                _sceneRebindId = "";
                return;
            }
            if (rebound.Target.Kind != target.Kind)
            {
                _notice = "Select the same object type as the original target.";
                return;
            }
            MapEdit(l => l.Objects.AsValueEnumerable().Single(o => o.Id == id).Target = target);
            _sceneRebindId = "";
            _sceneSelectionError = "";
            _sceneSelectionPose = null;
            _selected = id;
            _picked = root;
            _sceneTab = "Changes";
            Refresh();
            return;
        }
        _picked = root;
        _selected = "";
        var binding = error.Length == 0 ? (_mapScene ??= new()).CaptureOriginal(root!) : null;
        var record = Layout
            ?.Objects.AsValueEnumerable()
            .FirstOrDefault(o =>
                binding != null
                && o.Operation != "Copy"
                && o.Target.Kind == binding.Kind
                && o.Target.Path == binding.Path
                && o.Target.Scene == binding.Scene
            );
        if (record != null)
            _selected = record.Id;
        _sceneTab = "Existing";
        SetSceneSelectionPose(root!, binding, error);
        _notice = error.Length > 0 ? "Selected for inspection. " + error : "Selected " + root!.name;
        Refresh();
        _view?.Windows.ShowPanel("Inspector", true);
    }

    private void SceneTransform(string tool)
    {
        if (!CanSceneEdit)
            return;
        if (!CanTransformScene(tool))
            return;
        if (MapPoint is MapObjectEdit { Operation: "Hide" })
            return;
        _tool = tool;
    }

    private bool CanSceneEdit =>
        EditorMode.Ready
        && !_walking
        && _walkAssetLifetime == null
        && Layout != null
        && _session?.Definition != null
        && _session.Conflict == null
        && !_session.Retired;

    private void RemoveSceneObject()
    {
        if (!CanSceneEdit)
            return;
        if (MapPoint is MapLootPlacement or MapObjectEdit { Operation: "Copy" } or MapVolume)
            DeleteMapRecord();
        else if (MapPoint is MapObjectEdit edit)
            MapEdit(_ => edit.Operation = "Hide");
        else if (_picked)
            CaptureMapObject("Hide");
    }

    private async Task BeginPlacement()
    {
        if (!CanSceneEdit || _catalogSelection.Length == 0)
            return;
        CancelPlacement();
        var lifetime = _placementLifetime = new CancellationTokenSource();
        var token = lifetime.Token;
        try
        {
            GameObject model;
            if (_selectedCatalogEntry?.AssetTarget != null && AssetCatalog)
            {
                var error = CatalogError(_selectedCatalogEntry);
                if (error.Length > 0)
                    throw new InvalidOperationException(error);
                var loaded = await SceneAssetCatalog.Load(_selectedCatalogEntry.AssetTarget, token);
                if (token.IsCancellationRequested)
                {
                    loaded.Dispose();
                    return;
                }
                _placementAsset = loaded;
                model = loaded.Object;
                if (model.GetComponentInChildren<LootableContainer>(true) is { } container)
                    container.enabled = false;
            }
            else if (_sceneFilter == "Props" && _sceneRoots.TryGetValue(_catalogSelection, out var target) && target)
            {
                _placementProp = (_mapScene ??= new()).CaptureOriginal(target);
                model = _mapScene.CopyForPlacement(target);
            }
            else
            {
                _placementLoot = _selectedCatalogEntry;
                if (_placementLoot == null || _placementLoot.Error.Length > 0)
                    throw new InvalidOperationException(_placementLoot?.Error ?? "Select an available catalog item.");
                _notice = "Loading item model…";
                model = await SceneLootModel.Create(_placementLoot.Items, token);
                if (token.IsCancellationRequested)
                {
                    SceneLootModel.Release(model);
                    return;
                }
                _placementPooled = true;
            }
            if (token.IsCancellationRequested)
            {
                Destroy(model);
                return;
            }
            _placement = model;
            foreach (var collider in model.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            model.SetActive(false);
            _notice = "Point at a surface and click to place · Esc cancels";
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            if (ReferenceEquals(lifetime, _placementLifetime))
            {
                _notice = e.Message;
                CancelPlacement();
            }
        }
    }

    private bool PlacementInput()
    {
        if (_placementLifetime == null)
            return false;
        if (!CanSceneEdit || !SceneWorkspace)
        {
            CancelPlacement();
            return false;
        }
        if (!_placement || !_camera)
            return true;
        var overUi = EventSystem.current?.IsPointerOverGameObject() == true || _view?.PointerOver == true;
        var hitSurface =
            !overUi && Physics.Raycast(_camera!.ScreenPointToRay(Input.mousePosition), out _, 1000, ~0, QueryTriggerInteraction.Ignore);
        _placement!.SetActive(hitSurface);
        if (!hitSurface || Input.GetMouseButton(1))
            return true;
        Physics.Raycast(_camera!.ScreenPointToRay(Input.mousePosition), out var hit, 1000, ~0, QueryTriggerInteraction.Ignore);
        var position = hit.point;
        if (_snap && !Input.GetKey(KeyCode.LeftAlt))
            position = new Vector3(Mathf.Round(position.x * 20) / 20, Mathf.Round(position.y * 20) / 20, Mathf.Round(position.z * 20) / 20);
        _placement.transform.position = position;
        var renderers = _placement.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers)
                bounds.Encapsulate(renderer.bounds);
            var normal = hit.normal;
            var support = Vector3.Dot(new Vector3(Mathf.Abs(normal.x), Mathf.Abs(normal.y), Mathf.Abs(normal.z)), bounds.extents);
            position -= normal * (Vector3.Dot(bounds.center - hit.point, normal) - support);
            _placement.transform.position = position;
        }
        if (Input.GetMouseButtonDown(0))
        {
            var id = MapId();
            var rotation = ZoneRuntime.Vector(_placement.transform.eulerAngles);
            var scale = ZoneRuntime.Vector(_placement.transform.localScale);
            var prop = _placementProp;
            var loot = _placementLoot;
            var asset = _placementAsset != null ? _selectedCatalogEntry : null;
            MapEdit(l =>
            {
                if (asset?.AssetTarget != null)
                    l.Objects.Add(
                        new MapObjectEdit
                        {
                            Id = id,
                            Name = asset.Name,
                            Location = l.Location,
                            Scene = hit.transform.gameObject.scene.name,
                            Target = RaidEditorSession.Copy(asset.AssetTarget),
                            Operation = "Copy",
                            Position = ZoneRuntime.Vector(position),
                            Rotation = rotation,
                            Scale = asset.AssetTarget.Kind is "AssetContainer" or "Container"
                                ? new SpatialVector
                                {
                                    X = 1,
                                    Y = 1,
                                    Z = 1,
                                }
                                : scale,
                        }
                    );
                else if (prop != null)
                    l.Objects.Add(
                        new MapObjectEdit
                        {
                            Id = id,
                            Name = _sceneRoots[_catalogSelection].name,
                            Location = l.Location,
                            Scene = prop.Scene,
                            Target = prop,
                            Operation = "Copy",
                            Position = ZoneRuntime.Vector(position),
                            Rotation = rotation,
                            Scale = scale,
                        }
                    );
                else if (loot != null)
                    l.Loot.Add(
                        new MapLootPlacement
                        {
                            Id = id,
                            Name = loot.Name,
                            Location = l.Location,
                            Scene = hit.transform.gameObject.scene.name,
                            Position = ZoneRuntime.Vector(position),
                            Rotation = rotation,
                            Items = FreshItems(loot.Items),
                        }
                    );
                _selected = id;
            });
            CancelPlacement();
            _picked = null;
            _sceneSelectionPose = null;
            _sceneSelectionError = "";
            _tool = "Move";
            Refresh();
        }
        return true;
    }

    private static List<WTT.Campaigns.Shared.Native.NativeItem> FreshItems(List<WTT.Campaigns.Shared.Native.NativeItem> source)
    {
        var copy = RaidEditorSession.Copy(source);
        var ids = copy.AsValueEnumerable().ToDictionary(i => i.Id, _ => MapId());
        foreach (var item in copy)
        {
            var old = item.Id;
            item.Id = ids[old];
            if (item.ParentId != null && ids.TryGetValue(item.ParentId, out var parent))
                item.ParentId = parent;
        }
        return copy;
    }

    private void CancelPlacement()
    {
        _placementLifetime?.Cancel();
        _placementLifetime?.Dispose();
        _placementLifetime = null;
        if (_placementAsset != null)
        {
            _placementAsset.Dispose();
            _placementAsset = null;
        }
        else if (_placementPooled)
            SceneLootModel.Release(_placement);
        else if (_placement)
        {
            _placement!.SetActive(false);
            Destroy(_placement);
        }
        _placement = null;
        _placementProp = null;
        _placementLoot = null;
        _placementPooled = false;
    }

    private string CatalogError(SceneCatalogEntry entry) =>
        entry.Error.Length > 0 ? entry.Error
        : (entry.AssetTarget?.Kind is "AssetContainer" or "Container")
        && (_containerTemplates == null || !_containerTemplates.Contains(entry.AssetTarget.Template))
            ? (
                _containerTemplates == null
                    ? "Checking native container loot mapping�"
                    : "No native loot mapping is available for this container."
            )
        : "";

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
            if (sessionId != EditorMode.SessionId || _assetCatalog == null)
                return;
            if (response == null || response.Error != null)
                throw new InvalidOperationException(response?.Error ?? "No container response.");
            _containerTemplates = new HashSet<string>(response.Templates);
            _libraryKey = "";
        }
        catch (Exception e)
        {
            _notice = e.Message;
        }
    }

    private void ClearSceneCatalog()
    {
        _assetCatalog?.Dispose();
        _assetCatalog = null;
        _containerTemplates = null;
        _sceneRestrictionLog.Clear();
        _libraryKey = "";
        _sceneSelectionPose = null;
        _sceneSelectionError = "";
        _sceneRenderers.Clear();
        CancelPlacement();
        _sceneRebindId = "";
        _catalogGeneration++;
        _catalogKey = "";
        _catalog = null;
        _catalogSelection = "";
        _selectedCatalogEntry = null;
        _filterSelections.Clear();
        _unsupportedSceneIds.Clear();
        _localCatalogEntries.Clear();
        _sceneRoots.Clear();
        _discoveredRoots.Clear();
        _propShapes.Clear();
        _thumbnailLifetime.Cancel();
        _thumbnailLifetime.Dispose();
        _thumbnailLifetime = new();
        _previews.Clear();
        _thumbnailWorker = false;
        _thumbnailQueue.Clear();
    }
}
