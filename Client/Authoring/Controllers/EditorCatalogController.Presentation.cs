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

namespace WTT.Campaigns.Client.Authoring.Controllers;

internal sealed partial class EditorCatalogController
{
    internal string ToolkitSelection =>
        !InspectingScene
            ? _catalogSelection
            : _context.MapPoint?.Id ?? _context.MapDoor?.Id ?? (_context.Picked ? _context.Picked!.GetInstanceID().ToString() : "");

    internal string ToolkitContext =>
        !SceneWorkspace
            ? _context.ToolId + ":" + _context.LayoutId + ":" + _context.SelectionId
            : _catalogSource + ":" + _sceneTab + ":" + _sceneFilter + ":" + _context.LayoutId + ":" + ToolkitSelection;

    internal void PresentScene()
    {
        var view = _context.View!;
        var scene = SceneWorkspace;
        var sceneKind =
            _context.ScenePoint is MapObjectEdit objectEdit ? objectEdit.Operation
            : _context.ScenePoint is MapVolume volume && _context.Layout?.Barriers.AsValueEnumerable().Any(b => b.Id == volume.Id) == true
                ? "Barrier"
            : _context.ScenePoint is MapVolume ? "Volume"
            : _context.MapDoor?.PlaceNew == true ? "PlacedDoor"
            : _context.MapDoor != null || _context.PickedDoor ? "Door"
            : "Loot";
        view.Windows.PresentScene(
            scene,
            _sceneTab,
            sceneKind,
            !InspectingScene ? _catalogSelection.Length > 0 : _context.MapPoint != null || _context.MapDoor != null || _context.Picked,
            _context.ScenePoint != null || _context.MapDoor != null,
            CanSceneEdit,
            _context.Picked,
            _context.MapPoint != null,
            InspectingScene
        );
        if (!scene)
        {
            _thumbnailSchedule.Want(Array.Empty<string>());
            for (var i = 0; i < view.RowCapacity; i++)
            {
                view.Visible("SceneIcon" + i, false);
                view.Visible("SceneIconStatus" + i, false);
            }
            return;
        }
        foreach (var tab in new[] { "Catalog", "Existing", "Changes" })
            view.Highlight("Scene" + tab, _sceneTab == tab);
        var filters = SceneBrowserState.Filters.AsValueEnumerable().Where(f => SceneBrowserState.Available(_sceneTab, f)).ToArray();
        view.SetDropdown(
            "SceneFilter",
            filters.AsValueEnumerable().Select(f => new EditorChoice.OptionData(f)).ToList(),
            Array.IndexOf(filters, _sceneFilter)
        );
        view.SetDropdown(
            "SceneSource",
            new List<EditorChoice.OptionData> { new("All game"), new("Current map") },
            _catalogSource == "All game" ? 0 : 1
        );
        var point = _context.ScenePoint;
        var catalog = _sceneTab == "Catalog" && !InspectingScene;
        view.Highlight("SceneHideUnavailable", _sceneBrowser.HideUnavailable);
        view.Caption("SceneHideUnavailable", _sceneBrowser.HideUnavailable ? "Hide unavailable: On" : "Hide unavailable: Off");
        view.Visible("SceneHideUnavailable", _sceneTab == "Catalog");
        view.Checked("SceneRepeat", _repeatPlacement);
        view.Get<Button>("SceneRepeat").interactable = CanSceneEdit;
        var removed = sceneKind == "Hide";
        var selected = catalog ? _catalogSelection.Length > 0 : point != null || _context.MapDoor != null || _context.Picked;
        view.Get<Button>("ScenePlace").interactable =
            CanSceneEdit
            && selected
            && !_placementRequests.Active
            && (_selectedCatalogEntry == null || CatalogError(_selectedCatalogEntry).Length == 0);
        view.Get<Button>("SceneMove").interactable =
            CanSceneEdit && selected && sceneKind != "Door" && _context.SceneSelectionError.Length == 0;
        view.Get<Button>("SceneRotate").interactable =
            CanSceneEdit && selected && sceneKind != "Door" && _context.SceneSelectionError.Length == 0;
        view.Get<Button>("SceneScale").interactable = _context.CanTransformScene("Scale");
        view.Get<Button>("SceneRemove").interactable = CanSceneEdit && selected && _context.SceneSelectionError.Length == 0;
        view.Get<Button>("SceneRestore").interactable = CanSceneEdit && _context.MapPoint is MapObjectEdit { Operation: "Move" or "Hide" };
        view.Get<Button>("SceneRebind").interactable = CanSceneEdit && _context.MapPoint is MapObjectEdit;
        foreach (var tool in new[] { "Move", "Rotate", "Scale" })
            view.Get<Button>(tool).interactable = _context.CanTransformScene(tool);
        var name = catalog
            ? (
                (_sceneFilter == "Props" || _sceneFilter == "Doors") && _sceneRoots.TryGetValue(_catalogSelection, out var source) && source
                    ? source.name
                    : _selectedCatalogEntry?.Name
            ) ?? "Select an item"
            : point?.Name ?? _context.MapDoor?.Name ?? (_context.Picked ? _context.Picked!.name : "Select an object");
        view.Text("SceneHeading", name);
        view.Text(
            "SceneInfo",
            _context.Layout == null ? "Select or create a layout in Layouts first."
                : removed ? "Removed from this layout. Restore original returns it to its original position."
                : catalog
                    ? (
                        _selectedCatalogEntry != null && CatalogError(_selectedCatalogEntry).Length > 0
                            ? CatalogError(_selectedCatalogEntry)
                        : _placementRequests.Active
                            ? (
                                _repeatPlacement
                                    ? "Click surfaces to place copies · Escape finishes."
                                    : "Click a surface to place · Escape cancels."
                            )
                        : "Select an item to preview. Place or double-click to begin placement."
                    )
                : sceneKind == "Door" ? "Use Door state to cycle the saved native state. Remove clears it from this layout."
                : point == null ? "Choose Move or Rotate to edit this object. Remove hides it in this layout."
                : "Saved in " + _context.Layout.Name + ". Undo and redo restore scene changes."
        );
        view.Text(
            "LibraryCount",
            _catalogRequests.Loading && RemoteCatalog ? "Loading catalog…"
                : LibraryTotal == 0 ? "No matching objects"
                : LibraryTotal + " objects · " + (_context.Page + 1) + " / " + ((LibraryTotal + LibraryPageSize - 1) / LibraryPageSize)
        );
        if (
            catalog
            && _selectedCatalogEntry?.AssetTarget is { } levelTarget
            && levelTarget.Bundle.StartsWith(NativeLevelPropLibrary.Prefix, StringComparison.Ordinal)
            && !_placementRequests.Active
            && _context.Layout != null
        )
        {
            var error = CatalogError(_selectedCatalogEntry);
            view.Text(
                "SceneInfo",
                "Source: "
                    + NativeLevelPropLibrary.SourceDescription(levelTarget)
                    + "\n"
                    + (error.Length > 0 ? error : "Place or double-click to begin placement.")
            );
        }
        if (AssetCatalog)
            view.Text(
                "LibraryCount",
                LibraryTotal
                    + " objects · "
                    + _pendingCatalogEntries
                    + " pending · "
                    + (_levelQueryLoading ? "Searching level scenery… · " : "")
                    + NativeLevelPropLibrary.LevelCount
                    + " level files indexed"
            );
        view.Windows.Select(
            "Scene/" + _sceneTab,
            selected
                ? catalog
                    ? _catalogSelection
                    : _context.MapPoint?.Id ?? _context.MapDoor?.Id ?? (_context.Picked ? _context.Picked!.GetInstanceID().ToString() : "")
                : ""
        );
        PresentContainerControls();
        view.Get<Button>("SceneFrame").interactable = _context.CanFrameScene;
        view.Get<Button>("SceneAnchor").interactable = !_context.IsDragging && !_placementRequests.Active && !_context.Walking;
        view.Caption("SceneAnchor", _context.CenterAnchor ? "Anchor: Center" : "Anchor: Pivot");
        PresentSceneThumbnails();
        if (_sceneFilter == "Doors")
        {
            view.Visible("CatalogViews", false);
            view.Text("LibraryCount", _context.Rows.Count + " doors · grouped by scene");
            if (catalog && _sceneRoots.TryGetValue(_catalogSelection, out var doorSource) && doorSource)
            {
                var reason = SceneDoorPlacement.Restriction(doorSource);
                view.Get<Button>("ScenePlace").interactable = CanSceneEdit && !_placementRequests.Active && reason.Length == 0;
                view.Text(
                    "SceneInfo",
                    reason.Length > 0
                        ? reason
                        : "Place a new interactive door from this map. Configure its key and starting state after placement."
                );
            }
        }
        _context.PresentPickedProperties();
        _context.PresentDoorControls();
        if (_context.MapScene?.Loading == true)
            view.Text("SceneInfo", "Loading placed item models…");
        if (_context.SceneSelectionError.Length == 0 && _context.MapScene?.TargetErrors.Count > 0)
            view.Text("SceneInfo", _context.MapScene.TargetErrors.AsValueEnumerable().Take(2).JoinToString("\n"));
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
        internal float QueuedAt;
        internal bool Native => !Source && Entry?.AssetTarget == null;
        internal Transform? Source;
        internal WTT.Campaigns.Shared.Authoring.SceneCatalogEntry? Entry;
    }

    internal void PresentSceneThumbnails(bool inspector = true)
    {
        if (_context.View?.Valid != true || !SceneWorkspace || !_context.IsOpen)
        {
            _thumbnailSchedule.Want(Array.Empty<string>());
            return;
        }
        var view = _context.View;
        var catalog = _sceneTab == "Catalog";
        var wanted = new HashSet<string> { _sceneFilter + ":" + _catalogSelection };
        view.SetRowThumbnails(catalog);
        if (catalog && _sceneFilter != "Doors")
            for (var i = LibraryOffset; i < Math.Min(_context.Rows.Count, LibraryOffset + LibraryPageSize); i++)
                wanted.Add(_sceneFilter + ":" + _context.Rows[i].Id);
        if (!catalog)
            wanted.Clear();
        _thumbnailSchedule.Want(wanted);
        _previews.Protect(wanted);
        EditorDiagnostics.ThumbnailCache(_previews.Hits, _previews.Misses, _previews.Count, _thumbnailQueue.Count);
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
            var visible = catalog && _sceneFilter != "Doors" && i < LibraryPageSize && index < _context.Rows.Count;
            view.Visible("SceneIcon" + i, visible);
            if (!visible)
            {
                view.Visible("SceneIconStatus" + i, false);
                continue;
            }
            var id = _context.Rows[index].Id;
            var key = _sceneFilter + ":" + id;
            RequestThumbnail(id, key);
            SetThumbnail(view.Get<RawImage>("SceneIcon" + i), key);
            view.Text("SceneIconStatus" + i, _previews.Error(key).Length > 0 ? "N/A" : "…");
            view.Visible("SceneIconStatus" + i, _previews.Get(key) == null);
            view.Windows.SetTooltip(
                "Row" + i,
                _context.Rows[index].Label + (_previews.Error(key) is { Length: > 0 } error ? "\nPreview unavailable: " + error : "")
            );
        }
        if (!inspector)
            return;
        var selectedKey = _sceneFilter + ":" + _catalogSelection;
        SetThumbnail(view.Get<RawImage>("ScenePreview"), selectedKey);
        var failed = _previews.Error(selectedKey).Length > 0;
        view.Visible("ScenePreviewStatus", catalog && _previews.Get(selectedKey) == null);
        view.Text("ScenePreviewStatus", failed ? "Preview unavailable" : "Loading preview…");
        view.Visible(
            "ScenePreviewRetryGroup",
            catalog && (failed || (_selectedCatalogEntry != null && CatalogError(_selectedCatalogEntry).Length > 0))
        );
    }

    private void RetryThumbnail()
    {
        var key = _sceneFilter + ":" + _catalogSelection;
        if (AssetCatalog)
        {
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
        var job = new ThumbnailJob
        {
            Id = id,
            Key = key,
            QueuedAt = Time.realtimeSinceStartup,
        };
        if (id.StartsWith("scene:", StringComparison.Ordinal))
            _sceneRoots.TryGetValue(id.Substring("scene:".Length), out job.Source);
        else if ((_sceneFilter == "Props" || _sceneFilter == "Doors") && !AssetCatalog)
            _sceneRoots.TryGetValue(id, out job.Source);
        if (!job.Source)
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
        var schedule = _thumbnailSchedule;
        try
        {
            while ((_thumbnailQueue.Count > 0 || schedule.Active > 0) && !token.IsCancellationRequested)
            {
                await UniTask.NextFrame(cancellationToken: token);
                if (!_context.IsOpen || !SceneWorkspace || _sceneTab != "Catalog")
                    schedule.Want(Array.Empty<string>());
                var selected = _sceneFilter + ":" + _catalogSelection;
                for (var dispatched = 0; dispatched < 5; dispatched++)
                {
                    var index = schedule.Next(_thumbnailQueue, selected, j => j.Key, j => j.Native);
                    if (index < 0)
                        break;
                    var job = _thumbnailQueue[index];
                    _thumbnailQueue.RemoveAt(index);
                    if (!schedule.Wanted(job.Key))
                    {
                        _previews.Abandon(job.Key);
                        continue;
                    }
                    schedule.Start(job.Native);
                    _ = RunThumbnail(job, epoch, token, schedule);
                }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (epoch == _previews.Generation)
                _thumbnailWorker = false;
        }
    }

    private async Task RunThumbnail(ThumbnailJob job, int epoch, CancellationToken token, SceneThumbnailSchedule schedule)
    {
        var native = job.Native;
        Texture? texture = null;
        Action? unsubscribeIcon = null;
        SceneAssetCatalog.Model? loaded = null;
        try
        {
            EditorDiagnostics.ThumbnailWait((Time.realtimeSinceStartup - job.QueuedAt) * 1000);
            if (native && job.Entry != null)
            {
                var start = Time.realtimeSinceStartup;
                var icon = ItemViewFactory.LoadItemIcon(SceneLootModel.Item(job.Entry.Items));
                Exception? snapshotError = null;
                void CaptureIcon()
                {
                    if (texture || snapshotError != null || token.IsCancellationRequested || !schedule.Wanted(job.Key) || !icon.Sprite)
                        return;
                    try
                    {
                        texture = SceneThumbnailRenderer.Snapshot(icon.Sprite);
                    }
                    catch (Exception error)
                    {
                        snapshotError = error;
                    }
                }
                // Native generation can finish several icons in one frame using a shared target.
                // Snapshot in its notification, before the next icon reuses that texture.
                unsubscribeIcon = icon.Changed.Subscribe(CaptureIcon);
                CaptureIcon();
                while (!texture && snapshotError == null && Time.realtimeSinceStartup - start < 15)
                {
                    await UniTask.NextFrame(cancellationToken: token);
                    if (!schedule.Wanted(job.Key))
                        return;
                }
                if (snapshotError != null)
                    throw snapshotError;
                EditorDiagnostics.ThumbnailLoad((Time.realtimeSinceStartup - start) * 1000);
            }
            else
            {
                if (!job.Source && job.Entry?.AssetTarget != null)
                {
                    var start = Time.realtimeSinceStartup;
                    loaded = await SceneAssetCatalog.Load(job.Entry.AssetTarget, token, previewOnly: true);
                    EditorDiagnostics.ThumbnailLoad((Time.realtimeSinceStartup - start) * 1000);
                }
                token.ThrowIfCancellationRequested();
                // The loader can finish in the same frame as another synchronous scene preview.
                while (_thumbnailRenderFrame == Time.frameCount)
                    await UniTask.NextFrame(cancellationToken: token);
                if (!schedule.Wanted(job.Key))
                    return;
                _thumbnailRenderFrame = Time.frameCount;
                if (loaded != null)
                    texture = RenderPropThumbnail(loaded.Object.transform, loaded.Object);
                else if (job.Source)
                    texture = RenderPropThumbnail(job.Source!);
            }
            token.ThrowIfCancellationRequested();
            if (!schedule.Wanted(job.Key))
                return;
            if (!texture)
                throw new InvalidOperationException("No visible image was returned. Retry when the object is loaded.");
            _previews.Complete(
                epoch,
                job.Key,
                new ThumbnailImage
                {
                    Texture = texture!,
                    Uv = new Rect(0, 0, 1, 1),
                    Owned = true,
                },
                _sceneFilter + ":" + _catalogSelection
            );
            texture = null;
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            if (schedule.Wanted(job.Key))
                _previews.Fail(epoch, job.Key, e.Message);
        }
        finally
        {
            unsubscribeIcon?.Invoke();
            loaded?.Dispose();
            if (texture)
                SceneThumbnailRenderer.Release(texture!);
            schedule.Finish(native);
            if (epoch == _previews.Generation)
            {
                _previews.Abandon(job.Key);
                PresentSceneThumbnails();
            }
        }
    }

    private Texture RenderPropThumbnail(Transform source, GameObject? prepared = null)
    {
        GameObject model;
        using (EditorDiagnostics.Measure(EditorDiagnostics.Area.ThumbnailPrepare))
            model = prepared ?? ScenePreviewModel.Copy(source);
        try
        {
            _thumbnailRenderer ??= new SceneThumbnailRenderer(_context.View!.PreviewShader);
            return _thumbnailRenderer.Render(model);
        }
        finally
        {
            if (prepared == null)
                UnityEngine.Object.Destroy(model);
        }
    }
}
