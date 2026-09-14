namespace WTT.Campaigns.Client.Authoring;

public sealed partial class RaidEditor
{
    // Patrols use the same pooled canvas route renderer as player checkpoints.
    // DrawGeometry/RefreshAiWorkspace refresh it with the current typed layout;
    // this method remains a small lifecycle seam for Close and mode changes.
    private void RefreshAiRoutes()
    {
        if (!AiWorkspace || _view?.Valid != true || _camera == null || Layout == null)
            return;
        _view.DrawRoute(Layout, _camera, _selected, _session?.ContentVersion ?? 0);
    }

    private void ClearAiRoutes() => _view?.HideRoute();
}
