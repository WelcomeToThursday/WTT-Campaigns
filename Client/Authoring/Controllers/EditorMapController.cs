namespace WTT.Campaigns.Client.Authoring.Controllers;

internal sealed partial class EditorMapController : IDisposable
{
    private readonly IEditorMapContext _context;

    internal EditorMapController(IEditorMapContext context) => _context = context;

    internal void Reset()
    {
        _doorKeyGeneration++;
        _doorKeys.Clear();
        _doorKeyIndex = 0;
    }

    public void Dispose() => Reset();
}
