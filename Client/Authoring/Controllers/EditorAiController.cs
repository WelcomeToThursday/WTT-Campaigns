using WTT.Campaigns.Client.Authoring.Scenes;

namespace WTT.Campaigns.Client.Authoring.Controllers;

internal sealed partial class EditorAiController : IDisposable
{
    private readonly IEditorAiContext _context;
    private readonly Func<WTT.Campaigns.Shared.Spatial.MapLayout?> _layoutProvider;

    internal EditorAiController(IEditorAiContext context)
    {
        _context = context;
        _layoutProvider = () => _context.Layout;
    }

    internal bool InspectNavigation => _inspectAiNavigation;

    internal void Reset()
    {
        _aiSelectionKind = "";
        _inspectAiNavigation = false;
        _aiSpawnChoices.Clear();
        _aiPatrolChoices.Clear();
    }

    public void Dispose()
    {
        Reset();
        if (RaidEditorAiContracts.LayoutProvider == _layoutProvider)
            RaidEditorAiContracts.LayoutProvider = null;
    }
}
