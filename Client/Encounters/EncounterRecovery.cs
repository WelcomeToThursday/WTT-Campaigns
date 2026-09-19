using System.Net;
using System.Net.Http;

namespace WTT.Campaigns.Client.Encounters;

// Retry only transport work, before its result can enter native bot activation.
internal static class EncounterRecovery
{
    internal static async Task<T> RequestAsync<T>(
        Func<Task<T>> request,
        CancellationToken token,
        Action<int> retrying,
        Func<int, CancellationToken, Task> delay
    )
    {
        for (var attempt = 1; ; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var result = await request();
                token.ThrowIfCancellationRequested();
                return result;
            }
            catch (Exception error) when (!token.IsCancellationRequested && attempt < 3 && Transient(error))
            {
                retrying(attempt + 1);
                await delay(attempt * 500, token);
            }
        }
    }

    private static bool Transient(Exception error) =>
        error is TimeoutException or TaskCanceledException
        || error is HttpRequestException { InnerException: System.IO.IOException or System.Net.Sockets.SocketException }
        || error is WebException web
            && web.Status
                is WebExceptionStatus.Timeout
                    or WebExceptionStatus.ConnectFailure
                    or WebExceptionStatus.ConnectionClosed
                    or WebExceptionStatus.ReceiveFailure
                    or WebExceptionStatus.SendFailure;
}

// Each native activation is tracked before it starts, including one that fails mid-callback.
// Rollback attempts every cleanup even when an earlier item cannot be removed yet.
internal sealed class EncounterWaveCleanup
{
    private readonly List<Action> _undo = new();

    internal void Track(Action undo) => _undo.Add(undo);

    internal void Commit() => _undo.Clear();

    internal void Rollback()
    {
        var errors = new List<Exception>();
        for (var i = _undo.Count - 1; i >= 0; i--)
        {
            try
            {
                _undo[i]();
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
        }
        _undo.Clear();
        if (errors.Count > 0)
            throw new AggregateException("Incomplete wave cleanup needs a checkpoint retry or preview reset.", errors);
    }
}
