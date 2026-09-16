using System.Threading;

namespace WTT.Campaigns.Client.Authoring.Scenes;

// Owns only the result of one editor model request, including factories that complete after cancellation.
internal sealed class SceneModelLease<T> : IDisposable
    where T : class
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Action<T> _release;
    private bool _disposed;
    internal T? Model { get; private set; }
    internal bool Pending { get; private set; }
    internal string Error { get; private set; } = "";

    internal SceneModelLease(Action<T> release) => _release = release;

    internal async Task Load(Func<CancellationToken, Task<T>> create)
    {
        if (_disposed || Pending || Model != null)
            return;
        Pending = true;
        var token = _lifetime.Token;
        try
        {
            var model = await create(token);
            if (_disposed || token.IsCancellationRequested)
                _release(model);
            else
                Model = model;
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            if (!_disposed)
                Error = e.Message;
        }
        finally
        {
            Pending = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
        try
        {
            if (Model != null)
                _release(Model);
        }
        finally
        {
            Model = null;
        }
    }
}
