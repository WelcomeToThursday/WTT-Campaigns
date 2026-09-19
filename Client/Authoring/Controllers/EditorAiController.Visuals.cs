namespace WTT.Campaigns.Client.Authoring.Controllers;

internal sealed partial class EditorAiController
{
    // Patrols use the same pooled canvas route renderer as player checkpoints.
    // DrawGeometry/RefreshAiWorkspace refresh it with the current typed layout;
    // this method remains a small lifecycle seam for Close and mode changes.
    private void RefreshAiRoutes()
    {
        if (!AiWorkspace || _context.View?.Valid != true || _context.Camera == null || _context.Layout == null)
            return;
        _context.View.DrawRoute(_context.Layout, _context.Camera, _context.SelectionId, _context.Session?.ContentVersion ?? 0);
    }

    internal void ClearAiRoutes() => _context.View?.HideRoute();
}
