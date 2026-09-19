namespace WTT.Campaigns.Client.Profiles;

// A timed-out save remains owned by its original session. Retrying must not
// attach another native flush callback or continue a previous editor entry.
internal sealed class ProfileSaveGate
{
    private object? _owner;
    private Task? _pending;

    internal static bool IsEmpty(bool idle, bool flushing, int incoming, int unsent, bool waiting) =>
        idle && !flushing && incoming == 0 && unsent == 0 && !waiting;

    internal async Task Run(object owner, Func<bool> empty, Func<Task> save, Func<CancellationToken, Task> deadline)
    {
        if (!ReferenceEquals(_owner, owner) || _pending == null || _pending.IsCompleted)
        {
            if (empty())
                return;
            _owner = owner;
            _pending = save();
            _ = _pending.ContinueWith(
                t =>
                {
                    _ = t.Exception;
                },
                TaskContinuationOptions.OnlyOnFaulted
            );
        }

        var pending = _pending;
        using var lifetime = new CancellationTokenSource();
        var timeout = deadline(lifetime.Token);
        try
        {
            if (await Task.WhenAny(pending, timeout) != pending)
                throw new TimeoutException("Saving inventory did not finish. Remain in the menu and try again once the server responds.");
            await pending;
        }
        finally
        {
            lifetime.Cancel();
        }
    }
}
