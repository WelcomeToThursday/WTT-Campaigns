using EFT.Interactive;
using UnityEngine;
using UnityEngine.EventSystems;
using WTT.Campaigns.Client.Authoring.Scenes;
using WTT.Campaigns.Client.Spatial;
using WTT.Campaigns.Shared.Authoring;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Client.Authoring.Controllers;

internal sealed partial class EditorCatalogController
{
    private bool _repeatPlacement;

    private string _placementLastId = "";

    private SceneAssetCatalog.Model? _placementAsset;

    private GameObject? _placement;

    private MapTarget? _placementProp;

    private SceneCatalogEntry? _placementLoot;

    private readonly SceneOperationLifetime _placementRequests = new();

    private bool _placementPooled;

    private async Task BeginPlacement()
    {
        if (!SceneWorkspace || _sceneTab != "Catalog" || !CanSceneEdit || _catalogSelection.Length == 0)
            return;
        CancelPlacement();
        var token = _placementRequests.Begin();
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
            else if (
                (_sceneFilter == "Props" || _sceneFilter == "Doors")
                && _sceneRoots.TryGetValue(_catalogSelection, out var target)
                && target
            )
            {
                if (_sceneFilter == "Doors")
                {
                    var restriction = SceneDoorPlacement.Restriction(target);
                    if (restriction.Length > 0)
                        throw new InvalidOperationException(restriction);
                    _placementProp = MapSceneAdapter.Capture(target, true);
                }
                else
                    _placementProp = (_context.MapScene ??= new()).CaptureOriginal(target);
                _context.MapScene ??= new();
                model = _context.MapScene.CopyForPlacement(target);
            }
            else
            {
                _placementLoot = _selectedCatalogEntry;
                if (_placementLoot == null || _placementLoot.Error.Length > 0)
                    throw new InvalidOperationException(_placementLoot?.Error ?? "Select an available catalog item.");
                _context.Notice = "Loading item model…";
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
                UnityEngine.Object.Destroy(model);
                return;
            }
            _placement = model;
            foreach (var collider in model.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            model.SetActive(false);
            _context.Notice = "Point at a surface and click to place · Esc cancels";
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            if (_placementRequests.IsCurrent(token))
            {
                _context.Notice = e.Message;
                CancelPlacement();
            }
        }
    }

    internal bool PlacementInput()
    {
        if (!_placementRequests.Active)
            return false;
        if (!CanSceneEdit || !SceneWorkspace)
        {
            CancelPlacement();
            return false;
        }
        if (!_placement || !_context.Camera)
            return true;
        var overUi = EventSystem.current?.IsPointerOverGameObject() == true || _context.View?.PointerOver == true;
        var hitSurface =
            !overUi
            && Physics.Raycast(_context.Camera!.ScreenPointToRay(Input.mousePosition), out _, 1000, ~0, QueryTriggerInteraction.Ignore);
        _placement!.SetActive(hitSurface);
        if (!hitSurface || Input.GetMouseButton(1))
            return true;
        Physics.Raycast(_context.Camera!.ScreenPointToRay(Input.mousePosition), out var hit, 1000, ~0, QueryTriggerInteraction.Ignore);
        var position = hit.point;
        if (_context.Snap && !Input.GetKey(KeyCode.LeftAlt))
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
            var id = EditorMapRecords.NewId();
            var rotation = ZoneRuntime.Vector(_placement.transform.eulerAngles);
            var scale = ZoneRuntime.Vector(_placement.transform.localScale);
            var prop = _placementProp;
            var loot = _placementLoot;
            var asset = _placementAsset != null ? _selectedCatalogEntry : null;
            _context.MapEdit(l =>
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
                            Container = asset.AssetTarget.Kind is "AssetContainer" or "Container"
                                ? new ContainerSettings
                                {
                                    Mode = _containerTemplates?.Contains(asset.AssetTarget.Template) == true ? "Native" : "Empty",
                                }
                                : null,
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
                else if (prop?.Kind == "Door")
                    l.Doors.Add(
                        new MapDoorEdit
                        {
                            Id = id,
                            Name = _sceneRoots[_catalogSelection].name,
                            Location = l.Location,
                            Scene = prop.Scene,
                            Target = prop,
                            PlaceNew = true,
                            State = "Shut",
                            Position = ZoneRuntime.Vector(position),
                            Rotation = rotation,
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
                            Items = EditorMapRecords.FreshItems(loot.Items),
                        }
                    );
                _context.SelectionId = id;
            });
            _placementLastId = id;
            _context.Picked = null;
            _context.SceneSelectionPose = null;
            _context.SceneSelectionError = "";
            _context.TransformTool = "Move";
            if (!_repeatPlacement)
                CancelPlacement(inspectLast: true);
            _context.Refresh();
        }
        return true;
    }

    internal void CancelPlacement(bool inspectLast = false)
    {
        var lastId = _placementLastId;
        _placementLastId = "";
        _placementRequests.Cancel();
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
            UnityEngine.Object.Destroy(_placement);
        }
        _placement = null;
        _placementProp = null;
        _placementLoot = null;
        _placementPooled = false;
        if (inspectLast && lastId.Length > 0)
        {
            _sceneTab = "Existing";
            _sceneFilter = "All";
            _context.SelectionId = lastId;
            _context.Page = 0;
            _context.LibraryKey = "";
            _context.View?.Value("Search", "");
            _context.Refresh();
            _context.View?.Windows.ShowPanel("Inspector", true);
        }
    }

    private string CatalogError(SceneCatalogEntry entry) =>
        entry.Error.Length > 0 ? entry.Error
        : entry.AssetTarget?.Bundle != NativeContainerLibrary.BundleKey
        && (entry.AssetTarget?.Kind is "AssetContainer" or "Container")
        && (_containerTemplates == null || !_containerTemplates.Contains(entry.AssetTarget.Template))
            ? (
                _containerTemplates == null
                    ? "Checking native container loot mapping�"
                    : "No native loot mapping is available for this container."
            )
        : "";
}
