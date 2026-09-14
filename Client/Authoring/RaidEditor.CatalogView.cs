using Cysharp.Threading.Tasks;
using EFT.UI.DragAndDrop;
using UnityEngine;
using UnityEngine.UI;
using WTT.Campaigns.Shared.Spatial;
using ZLinq;

namespace WTT.Campaigns.Client.Authoring;

public sealed partial class RaidEditor
{
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
            for (var i = 0; i < 10; i++)
            {
                view.Visible("SceneIcon" + i, false);
                view.Visible("SceneIconStatus" + i, false);
                WTT.Campaigns.UI.Controls.UiElements.Stretch(
                    view.Get<Button>("Row" + i).GetComponentInChildren<Text>(true).rectTransform,
                    8,
                    8,
                    2,
                    2
                );
            }
            return;
        }
        foreach (var tab in new[] { "Catalog", "Existing", "Changes" })
            view.Highlight("Scene" + tab, _sceneTab == tab);
        foreach (var filter in new[] { "Props", "Loot", "Presets" })
            view.Highlight("Scene" + filter, _sceneFilter == filter);
        var point = ScenePoint;
        var catalog = _sceneTab == "Catalog";
        var removed = sceneKind == "Hide";
        var selected = catalog ? _catalogSelection.Length > 0 : point != null || MapDoor != null || _picked;
        view.Get<Button>("ScenePlace").interactable = CanSceneEdit && selected && _placementLifetime == null;
        view.Get<Button>("SceneMove").interactable =
            CanSceneEdit && selected && sceneKind != "Door" && _sceneSelectionError.Length == 0;
        view.Get<Button>("SceneRotate").interactable =
            CanSceneEdit && selected && sceneKind != "Door" && _sceneSelectionError.Length == 0;
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
                : catalog ? "Place on a surface, then refine with the transform handles. Escape cancels."
                : sceneKind == "Door" ? "Use Door state to cycle the saved native state. Remove clears it from this layout."
                : point == null ? "Choose Move or Rotate to edit this object. Remove hides it in this layout."
                : "Saved in " + Layout.Name + ". Undo and redo restore scene changes."
        );
        view.Text(
            "LibraryCount",
            _catalogLoading && RemoteCatalog ? "Loading catalog…"
                : LibraryTotal == 0 ? "No matching objects"
                : LibraryTotal + " objects · " + (_page + 1) + " / " + ((LibraryTotal + 9) / 10)
        );
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
        var texture = image.texture;
        if (texture)
        {
            var ratio = texture!.width * image.uvRect.width / Mathf.Max(1, texture.height * image.uvRect.height);
            var width = image.name == "ScenePreview" ? 300f : 40f;
            var height = image.name == "ScenePreview" ? 180f : 40f;
            image.rectTransform.sizeDelta =
                ratio > width / height ? new Vector2(width, width / ratio) : new Vector2(height * ratio, height);
        }
    }

    private sealed class ThumbnailJob
    {
        internal string Id = "",
            Key = "";
        internal Transform? Source;
        internal WTT.Campaigns.Shared.Authoring.SceneCatalogEntry? Entry;
    }

    private void PresentSceneThumbnails()
    {
        if (_view?.Valid != true || !SceneWorkspace)
            return;
        var view = _view;
        var catalog = _sceneTab == "Catalog";
        var wanted = new HashSet<string> { _sceneFilter + ":" + _catalogSelection };
        if (catalog)
            for (var i = LibraryOffset; i < Math.Min(_rows.Count, LibraryOffset + 10); i++)
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
        for (var i = 0; i < 10; i++)
        {
            var index = LibraryOffset + i;
            var visible = catalog && index < _rows.Count;
            view.Visible("SceneIcon" + i, visible);
            var label = view.Get<Button>("Row" + i).GetComponentInChildren<Text>(true);
            WTT.Campaigns.UI.Controls.UiElements.Stretch(label.rectTransform, visible ? 54 : 8, 8, 2, 2);
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
        var selectedKey = _sceneFilter + ":" + _catalogSelection;
        SetThumbnail(view.Get<RawImage>("ScenePreview"), selectedKey);
        var failed = _previews.Error(selectedKey).Length > 0;
        view.Visible("ScenePreviewStatus", catalog && _previews.Get(selectedKey) == null);
        view.Text("ScenePreviewStatus", failed ? "Preview unavailable" : "Loading preview…");
        view.Visible("ScenePreviewRetryGroup", catalog && failed);
    }

    private void RetryThumbnail()
    {
        var key = _sceneFilter + ":" + _catalogSelection;
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
        if (_sceneFilter == "Props")
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
                    else if (job.Entry != null)
                    {
                        var icon = ItemViewFactory.LoadItemIcon(SceneLootModel.Item(job.Entry.Items));
                        var deadline = Time.realtimeSinceStartup + 15;
                        while (!icon.Sprite && Time.realtimeSinceStartup < deadline)
                            await UniTask.NextFrame(cancellationToken: token);
                        if (icon.Sprite)
                        {
                            texture = icon.Sprite.texture;
                            var rect = icon.Sprite.textureRect;
                            uv = new Rect(
                                rect.x / texture.width,
                                rect.y / texture.height,
                                rect.width / texture.width,
                                rect.height / texture.height
                            );
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

    private Texture RenderPropThumbnail(Transform source)
    {
        var model = (_mapScene ??= new()).CopyForPlacement(source);
        var rig = new GameObject("CampaignEditor thumbnail camera");
        var rt = new RenderTexture(192, 192, 24);
        var materials = new List<Material>();
        Texture2D? texture = null;
        var previous = RenderTexture.active;
        try
        {
            model.transform.SetPositionAndRotation(new Vector3(0, -10000, 0), Quaternion.identity);
            foreach (var lod in model.GetComponentsInChildren<LODGroup>(true))
                if (lod && lod.enabled && lod.gameObject.activeInHierarchy)
                    lod.ForceLOD(0);
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
                    if (original && original.HasProperty("_MainTex"))
                    {
                        material.SetTexture("_MainTex", original.GetTexture("_MainTex"));
                        material.SetTextureScale("_MainTex", original.GetTextureScale("_MainTex"));
                        material.SetTextureOffset("_MainTex", original.GetTextureOffset("_MainTex"));
                    }
                    material.SetColor("_Color", original && original.HasProperty("_Color") ? original.GetColor("_Color") : Color.white);
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
            rt.Release();
            Destroy(rt);
        }
    }
}
