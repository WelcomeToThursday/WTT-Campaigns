using Cysharp.Threading.Tasks;
using EFT.Interactive;
using EFT.UI.DragAndDrop;
using UnityEngine;
using WTT.Campaigns.Client.Authoring.Scenes;
using WTT.Campaigns.Client.Authoring.Views;
using WTT.Campaigns.Shared.Spatial;
using ZLinq;
using Button = WTT.Campaigns.Client.Authoring.Views.EditorButton;
using RawImage = WTT.Campaigns.Client.Authoring.Views.EditorImage;
using Text = WTT.Campaigns.Client.Authoring.Views.EditorLabel;

namespace WTT.Campaigns.Client.Authoring;

public sealed partial class RaidEditor
{
    private string ToolkitSelection =>
        _sceneTab == "Catalog" ? _catalogSelection : MapPoint?.Id ?? MapDoor?.Id ?? (_picked ? _picked!.GetInstanceID().ToString() : "");
    private string ToolkitContext =>
        !SceneWorkspace
            ? _mode + ":" + _layoutId + ":" + _selected
            : _catalogSource + ":" + _sceneTab + ":" + _sceneFilter + ":" + _layoutId + ":" + ToolkitSelection;

    private void PresentScene()
    {
        var view = _view!;
        var scene = SceneWorkspace;
        var sceneKind =
            ScenePoint is MapObjectEdit objectEdit ? objectEdit.Operation
            : ScenePoint is MapVolume volume && Layout?.Barriers.AsValueEnumerable().Any(b => b.Id == volume.Id) == true ? "Barrier"
            : ScenePoint is MapVolume ? "Volume"
            : MapDoor != null ? "Door"
            : "Loot";
        view.Windows.PresentScene(
            scene,
            _sceneTab,
            sceneKind,
            _sceneTab == "Catalog" ? _catalogSelection.Length > 0 : MapPoint != null || MapDoor != null || _picked,
            ScenePoint != null || MapDoor != null,
            CanSceneEdit,
            _picked,
            MapPoint != null
        );
        if (!scene)
        {
            for (var i = 0; i < view.RowCapacity; i++)
            {
                view.Visible("SceneIcon" + i, false);
                view.Visible("SceneIconStatus" + i, false);
            }
            return;
        }
        foreach (var tab in new[] { "Catalog", "Existing", "Changes" })
            view.Highlight("Scene" + tab, _sceneTab == tab);
        foreach (var filter in new[] { "Props", "Containers", "Loot", "Presets" })
            view.Highlight("Scene" + filter, _sceneFilter == filter);
        view.SetDropdown(
            "SceneSource",
            new List<EditorChoice.OptionData> { new("All game"), new("Current map") },
            _catalogSource == "All game" ? 0 : 1
        );
        var point = ScenePoint;
        var catalog = _sceneTab == "Catalog";
        var removed = sceneKind == "Hide";
        var selected = catalog ? _catalogSelection.Length > 0 : point != null || MapDoor != null || _picked;
        view.Get<Button>("ScenePlace").interactable =
            CanSceneEdit
            && selected
            && _placementLifetime == null
            && (_selectedCatalogEntry == null || CatalogError(_selectedCatalogEntry).Length == 0);
        view.Get<Button>("SceneMove").interactable = CanSceneEdit && selected && sceneKind != "Door" && _sceneSelectionError.Length == 0;
        view.Get<Button>("SceneRotate").interactable = CanSceneEdit && selected && sceneKind != "Door" && _sceneSelectionError.Length == 0;
        view.Get<Button>("SceneScale").interactable = CanTransformScene("Scale");
        view.Get<Button>("SceneRemove").interactable = CanSceneEdit && selected && _sceneSelectionError.Length == 0;
        view.Get<Button>("SceneRestore").interactable = CanSceneEdit && MapPoint is MapObjectEdit { Operation: "Move" or "Hide" };
        view.Get<Button>("SceneRebind").interactable = CanSceneEdit && MapPoint is MapObjectEdit;
        foreach (var tool in new[] { "Move", "Rotate", "Scale" })
            view.Get<Button>(tool).interactable = CanTransformScene(tool);
        var name = catalog
            ? (
                _sceneFilter == "Props" && _sceneRoots.TryGetValue(_catalogSelection, out var source) && source
                    ? source.name
                    : _selectedCatalogEntry?.Name
            ) ?? "Select an item"
            : point?.Name ?? MapDoor?.Name ?? (_picked ? _picked!.name : "Select an object");
        view.Text("SceneHeading", name);
        view.Text(
            "SceneInfo",
            Layout == null ? "Select or create a layout in Layouts first."
                : removed ? "Removed from this layout. Restore original returns it to its original position."
                : catalog
                    ? (
                        _selectedCatalogEntry != null && CatalogError(_selectedCatalogEntry).Length > 0
                            ? CatalogError(_selectedCatalogEntry)
                            : "Place on a surface, then refine with the transform handles. Escape cancels."
                    )
                : sceneKind == "Door" ? "Use Door state to cycle the saved native state. Remove clears it from this layout."
                : point == null ? "Choose Move or Rotate to edit this object. Remove hides it in this layout."
                : "Saved in " + Layout.Name + ". Undo and redo restore scene changes."
        );
        view.Text(
            "LibraryCount",
            _catalogLoading && RemoteCatalog ? "Loading catalog…"
                : LibraryTotal == 0 ? "No matching objects"
                : LibraryTotal + " objects · " + (_page + 1) + " / " + ((LibraryTotal + LibraryPageSize - 1) / LibraryPageSize)
        );
        if (AssetCatalog)
            view.Text("LibraryCount", LibraryTotal + " objects � " + _assetCatalog?.Status);
        view.Windows.Select(
            "Scene/" + _sceneTab,
            selected
                ? catalog
                    ? _catalogSelection
                    : MapPoint?.Id ?? MapDoor?.Id ?? (_picked ? _picked!.GetInstanceID().ToString() : "")
                : ""
        );
        view.Get<Button>("SceneFrame").interactable = CanFrameScene;
        view.Get<Button>("SceneAnchor").interactable = _drag == null && _placementLifetime == null && !_walking;
        view.Caption("SceneAnchor", _centerAnchor ? "Anchor: Center" : "Anchor: Pivot");
        PresentSceneThumbnails();
        PresentPickedProperties();
        if (_mapScene?.Loading == true)
            view.Text("SceneInfo", "Loading placed item models…");
        if (_sceneSelectionError.Length == 0 && _mapScene?.TargetErrors.Count > 0)
            view.Text("SceneInfo", _mapScene.TargetErrors.AsValueEnumerable().Take(2).JoinToString("\n"));
    }

    private void SetThumbnail(RawImage image, string key)
    {
        var preview = _previews.Get(key);
        image.texture = preview?.Texture;
        image.uvRect = preview?.Uv ?? new Rect(0, 0, 1, 1);
        image.color = image.texture ? Color.white : Color.clear;
    }

    private sealed class ThumbnailJob
    {
        internal string Id = "",
            Key = "";
        internal Transform? Source;
        internal WTT.Campaigns.Shared.Authoring.SceneCatalogEntry? Entry;
    }

    private void PresentSceneThumbnails(bool inspector = true)
    {
        if (_view?.Valid != true || !SceneWorkspace)
            return;
        var view = _view;
        var catalog = _sceneTab == "Catalog";
        var wanted = new HashSet<string> { _sceneFilter + ":" + _catalogSelection };
        view.SetRowThumbnails(catalog);
        if (catalog)
            for (var i = LibraryOffset; i < Math.Min(_rows.Count, LibraryOffset + LibraryPageSize); i++)
                wanted.Add(_sceneFilter + ":" + _rows[i].Id);
        for (var i = _thumbnailQueue.Count - 1; i >= 0; i--)
            if (!catalog || !wanted.Contains(_thumbnailQueue[i].Key))
            {
                _previews.Abandon(_thumbnailQueue[i].Key);
                _thumbnailQueue.RemoveAt(i);
            }
        // Enqueue the selection first; jobs carry immutable category/source data.
        if (catalog && _catalogSelection.Length > 0)
            RequestThumbnail(_catalogSelection, _sceneFilter + ":" + _catalogSelection);
        for (var i = 0; i < view.RowCapacity; i++)
        {
            var index = LibraryOffset + i;
            var visible = catalog && i < LibraryPageSize && index < _rows.Count;
            view.Visible("SceneIcon" + i, visible);
            if (!visible)
            {
                view.Visible("SceneIconStatus" + i, false);
                continue;
            }
            var id = _rows[index].Id;
            var key = _sceneFilter + ":" + id;
            RequestThumbnail(id, key);
            SetThumbnail(view.Get<RawImage>("SceneIcon" + i), key);
            view.Text("SceneIconStatus" + i, _previews.Error(key).Length > 0 ? "N/A" : "…");
            view.Visible("SceneIconStatus" + i, _previews.Get(key) == null);
            view.Windows.SetTooltip(
                "Row" + i,
                _rows[index].Label + (_previews.Error(key) is { Length: > 0 } error ? "\nPreview unavailable: " + error : "")
            );
        }
        if (!inspector)
            return;
        var selectedKey = _sceneFilter + ":" + _catalogSelection;
        SetThumbnail(view.Get<RawImage>("ScenePreview"), selectedKey);
        var failed =
            _previews.Error(selectedKey).Length > 0 || (_selectedCatalogEntry != null && CatalogError(_selectedCatalogEntry).Length > 0);
        view.Visible("ScenePreviewStatus", catalog && _previews.Get(selectedKey) == null);
        view.Text("ScenePreviewStatus", failed ? "Preview unavailable" : "Loading preview…");
        view.Visible("ScenePreviewRetryGroup", catalog && failed);
    }

    private void RetryThumbnail()
    {
        var key = _sceneFilter + ":" + _catalogSelection;
        if (AssetCatalog)
        {
            _selectedCatalogEntry = null;
            _assetCatalog?.Retry(
                _catalogSelection,
                () =>
                {
                    _libraryKey = "";
                }
            );
            _ = LoadContainerTemplates();
        }
        if (_previews.Error(key).Length == 0)
            return;
        _previews.Retry(key);
        RequestThumbnail(_catalogSelection, key);
        PresentSceneThumbnails();
    }

    private void RequestThumbnail(string id, string key)
    {
        if (!_previews.Request(key))
            return;
        var job = new ThumbnailJob { Id = id, Key = key };
        if (_sceneFilter == "Props" && !AssetCatalog)
            _sceneRoots.TryGetValue(id, out job.Source);
        else
        {
            var entry =
                _selectedCatalogEntry?.Id == id
                    ? _selectedCatalogEntry
                    : _catalog?.Entries.AsValueEnumerable().FirstOrDefault(e => e.Id == id);
            if (entry != null)
                job.Entry = RaidEditorSession.Copy(entry);
        }
        if (!job.Source && job.Entry == null)
        {
            _previews.Fail(_previews.Generation, key, "The source object is no longer available.");
            return;
        }
        _thumbnailQueue.Add(job);
        if (!_thumbnailWorker)
            _ = RunThumbnailQueue();
    }

    private async Task RunThumbnailQueue()
    {
        _thumbnailWorker = true;
        var epoch = _previews.Generation;
        var token = _thumbnailLifetime.Token;
        try
        {
            while (_thumbnailQueue.Count > 0 && !token.IsCancellationRequested)
            {
                // At most one prop render in a frame, regardless of how many rows requested it.
                await UniTask.NextFrame(cancellationToken: token);
                if (_thumbnailQueue.Count == 0)
                    break;
                var selected = _sceneFilter + ":" + _catalogSelection;
                var index = _thumbnailQueue.FindIndex(j => j.Key == selected);
                if (index < 0)
                    index = 0;
                var job = _thumbnailQueue[index];
                _thumbnailQueue.RemoveAt(index);
                Texture? texture = null;
                var owned = false;
                var uv = new Rect(0, 0, 1, 1);
                try
                {
                    if (job.Source)
                    {
                        texture = RenderPropThumbnail(job.Source!);
                        owned = true;
                    }
                    else if (job.Entry?.AssetTarget != null)
                    {
                        if (job.Entry.Error.Length > 0)
                            throw new InvalidOperationException(job.Entry.Error);
                        using var model = await SceneAssetCatalog.Load(job.Entry.AssetTarget, token);
                        texture = RenderPropThumbnail(model.Object.transform, model.Object);
                        owned = true;
                    }
                    else if (job.Entry != null)
                    {
                        var icon = ItemViewFactory.LoadItemIcon(SceneLootModel.Item(job.Entry.Items));
                        var deadline = Time.realtimeSinceStartup + 15;
                        while (!icon.Sprite && Time.realtimeSinceStartup < deadline)
                            await UniTask.NextFrame(cancellationToken: token);
                        if (icon.Sprite)
                        {
                            // Native icons can live in a shared, reused render target.
                            // Retain an owned snapshot, not that mutable backing texture.
                            texture = SnapshotThumbnail(icon.Sprite);
                            owned = true;
                        }
                    }
                    token.ThrowIfCancellationRequested();
                    if (!texture)
                        throw new InvalidOperationException("No visible image was returned. Retry when the object is loaded.");
                    _previews.Complete(
                        epoch,
                        job.Key,
                        new ThumbnailImage
                        {
                            Texture = texture!,
                            Uv = uv,
                            Owned = owned,
                        },
                        selected
                    );
                    owned = false;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    _previews.Fail(epoch, job.Key, e.Message);
                }
                finally
                {
                    if (owned && texture)
                        Destroy(texture);
                }
                if (epoch == _previews.Generation)
                    PresentSceneThumbnails();
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (epoch == _previews.Generation)
                _thumbnailWorker = false;
        }
    }

    private Texture RenderPropThumbnail(Transform source, GameObject? prepared = null)
    {
        var model = prepared ?? (_mapScene ??= new()).CopyForPlacement(source);
        var rig = new GameObject("CampaignEditor thumbnail camera");
        var rt = new RenderTexture(192, 192, 24);
        var materials = new List<Material>();
        var opaqueTextures = new Dictionary<Texture, Texture2D>();
        Texture2D? texture = null;
        var previous = RenderTexture.active;
        try
        {
            if (model.GetComponentInChildren<LootableContainer>(true) is { } container)
                container.enabled = false;
            model.SetActive(true);
            model.transform.SetPositionAndRotation(new Vector3(0, -10000, 0), Quaternion.identity);
            foreach (var lod in model.GetComponentsInChildren<LODGroup>(true))
                if (lod && lod.enabled && lod.gameObject.activeInHierarchy)
                {
                    lod.fadeMode = LODFadeMode.None;
                    lod.animateCrossFading = false;
                    lod.ForceLOD(0);
                }
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = 31;
            if (!SceneBounds.TryGet(model.transform, out var bounds))
                throw new InvalidOperationException("Prop has no visible mesh.");
            foreach (var renderer in model.GetComponentsInChildren<Renderer>())
            {
                var originals = renderer.sharedMaterials;
                var preview = new Material[originals.Length];
                for (var i = 0; i < originals.Length; i++)
                {
                    var original = originals[i];
                    var material = new Material(_view!.PreviewShader);
                    materials.Add(material);
                    var opacity =
                        original
                        && PreviewMaterialPolicy.UsesOpacity(
                            original.GetTag("RenderType", false, ""),
                            original.shader ? original.shader.name : "",
                            original.IsKeywordEnabled("_ALPHATEST_ON"),
                            original.IsKeywordEnabled("_ALPHABLEND_ON") || original.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON")
                        );
                    if (original && original.HasProperty("_MainTex"))
                    {
                        var diffuse = original.GetTexture("_MainTex");
                        if (diffuse && !opacity)
                        {
                            if (!opaqueTextures.TryGetValue(diffuse, out var opaque))
                            {
                                opaque = SnapshotOpaqueDiffuse(diffuse);
                                opaqueTextures.Add(diffuse, opaque);
                            }
                            diffuse = opaque;
                        }
                        material.SetTexture("_MainTex", diffuse);
                        material.SetTextureScale("_MainTex", original.GetTextureScale("_MainTex"));
                        material.SetTextureOffset("_MainTex", original.GetTextureOffset("_MainTex"));
                    }
                    var tint = original && original.HasProperty("_Color") ? original.GetColor("_Color") : Color.white;
                    if (!opacity)
                        tint.a = 1;
                    material.SetColor("_Color", tint);
                    preview[i] = material;
                }
                renderer.sharedMaterials = preview;
            }
            var camera = rig.AddComponent<Camera>();
            camera.enabled = false;
            camera.cullingMask = 1 << 31;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.09f, .095f, .09f, 1);
            camera.orthographic = true;
            camera.orthographicSize = Mathf.Max(.1f, bounds.extents.magnitude * 1.1f);
            camera.nearClipPlane = .01f;
            camera.farClipPlane = Mathf.Max(20, bounds.size.magnitude * 6);
            camera.transform.position = bounds.center + new Vector3(1, .7f, -1).normalized * Mathf.Max(2, bounds.size.magnitude * 2);
            camera.transform.LookAt(bounds.center);
            camera.targetTexture = rt;
            rt.Create();
            camera.Render();
            RenderTexture.active = rt;
            texture = new Texture2D(192, 192, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(0, 0, 192, 192), 0, 0);
            texture.Apply();
            return texture;
        }
        catch
        {
            if (texture)
                Destroy(texture);
            throw;
        }
        finally
        {
            RenderTexture.active = previous;
            model.SetActive(false);
            rig.SetActive(false);
            Destroy(model);
            Destroy(rig);
            foreach (var material in materials)
                Destroy(material);
            foreach (var opaque in opaqueTextures.Values)
                Destroy(opaque);
            rt.Release();
            Destroy(rt);
        }
    }

    // Opaque EFT diffuse alpha can contain surface masks rather than opacity.
    // RGB24 drops that channel before the bundled preview shader's alpha test.
    // Blitting also supports source textures that cannot be read by the CPU.
    private static Texture2D SnapshotOpaqueDiffuse(Texture source)
    {
        var factor = Mathf.Min(1, 512f / Mathf.Max(source.width, source.height));
        var width = Mathf.Max(1, Mathf.RoundToInt(source.width * factor));
        var height = Mathf.Max(1, Mathf.RoundToInt(source.height * factor));
        var previous = RenderTexture.active;
        var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
        Texture2D? result = null;
        try
        {
            Graphics.Blit(source, target);
            RenderTexture.active = target;
            result = new Texture2D(width, height, TextureFormat.RGB24, false)
            {
                wrapModeU = source.wrapModeU,
                wrapModeV = source.wrapModeV,
                filterMode = source.filterMode,
            };
            result.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            result.Apply(false, true);
            return result;
        }
        catch
        {
            if (result)
                Destroy(result);
            throw;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
        }
    }

    private static Texture2D SnapshotThumbnail(Sprite sprite)
    {
        var source = sprite.texture;
        var rect = sprite.textureRect;
        var scale = Mathf.Min(1, 192f / Mathf.Max(rect.width, rect.height));
        var width = Mathf.Max(1, Mathf.RoundToInt(rect.width * scale));
        var height = Mathf.Max(1, Mathf.RoundToInt(rect.height * scale));
        var previous = RenderTexture.active;
        var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
        Texture2D? result = null;
        try
        {
            Graphics.Blit(
                source,
                target,
                new Vector2(rect.width / source.width, rect.height / source.height),
                new Vector2(rect.x / source.width, rect.y / source.height)
            );
            RenderTexture.active = target;
            result = new Texture2D(width, height, TextureFormat.RGBA32, false);
            result.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            result.Apply();
            return result;
        }
        catch
        {
            if (result)
                Destroy(result);
            throw;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
        }
    }
}
