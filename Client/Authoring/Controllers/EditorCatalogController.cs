namespace WTT.Campaigns.Client.Authoring.Controllers;

internal sealed partial class EditorCatalogController : IDisposable
{
    private readonly IEditorCatalogContext _context;
    private bool _disposed;

    internal EditorCatalogController(IEditorCatalogContext context) => _context = context;

    internal string SceneTab
    {
        get => _sceneTab;
        set => _sceneTab = value;
    }
    internal bool InspectingScene =>
        SceneWorkspace && (_sceneTab != "Catalog" || _context.Picked || _context.MapPoint != null || _context.MapDoor != null);
    internal string SceneFilter => _sceneFilter;
    internal string CatalogSource => _catalogSource;
    internal string CatalogSelection => _catalogSelection;
    internal string RebindId
    {
        get => _sceneRebindId;
        set => _sceneRebindId = value;
    }
    internal bool Placing => _placementRequests.Active;
    internal int CatalogPageSize
    {
        get => _catalogPageSize;
        set => _catalogPageSize = value;
    }
    internal bool HideUnavailable => _sceneBrowser.HideUnavailable;
    internal int ViewRevision => _catalogViewRevision;
    internal int CatalogGeneration => _catalogRequests.Generation;
    internal bool CatalogLoading => _catalogRequests.Loading;
    internal IReadOnlyDictionary<string, UnityEngine.Transform> SceneRoots => _sceneRoots;

    internal void RememberSceneTarget(UnityEngine.Transform target) => _sceneRoots[target.GetInstanceID().ToString()] = target;

    public void Dispose()
    {
        if (_disposed)
            return;
        Reset();
        _disposed = true;
        _thumbnailLifetime.Cancel();
        _thumbnailLifetime.Dispose();
        _levelSearchLifetime.Dispose();
    }
}
