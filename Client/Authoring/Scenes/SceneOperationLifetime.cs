namespace WTT.Campaigns.Client.Authoring.Scenes;

// Resource release remains with the caller; this owns only the pending operation.
internal sealed class SceneOperationLifetime : IDisposable
{
    private CancellationTokenSource? _source;

    internal bool Active => _source != null;

    internal CancellationToken Begin()
    {
        Cancel();
        _source = new();
        return _source.Token;
    }

    internal bool IsCurrent(CancellationToken token) => _source != null && _source.Token == token && !token.IsCancellationRequested;

    internal void Cancel()
    {
        var source = _source;
        _source = null;
        if (source == null)
            return;
        source.Cancel();
        source.Dispose();
    }

    public void Dispose() => Cancel();
}
