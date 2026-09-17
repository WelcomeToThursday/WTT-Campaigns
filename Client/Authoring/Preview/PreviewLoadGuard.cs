namespace WTT.Campaigns.Client.Authoring.Preview;

/// <summary>Bounds native loading even when a loader fails to complete its cancellation callback.</summary>
internal static class PreviewLoadGuard
{
    internal static async Task Run(
        Func<CancellationToken, Task> load,
        Func<CancellationToken, Task> deadline,
        CancellationToken token,
        Func<string> timeoutMessage
    )
    {
        token.ThrowIfCancellationRequested();
        using var operationLifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        using var timerLifetime = new CancellationTokenSource();
        var cancelled = new TaskCompletionSource<bool>();
        using var registration = token.Register(() => cancelled.TrySetResult(true));
        var operation = load(operationLifetime.Token);
        var timer = deadline(timerLifetime.Token);
        try
        {
            var completed = await Task.WhenAny(operation, timer, cancelled.Task);
            token.ThrowIfCancellationRequested();
            if (completed != operation)
            {
                await timer;
                throw new TimeoutException(timeoutMessage());
            }
            await operation;
            token.ThrowIfCancellationRequested();
        }
        finally
        {
            operationLifetime.Cancel();
            timerLifetime.Cancel();
            // Native pools may finish after cancellation. Observe their result, but never resume equipment changes.
            _ = Observe(operation);
            _ = Observe(timer);
        }
    }

    private static async Task Observe(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        { /* The active await reports errors; retired work has no right to alter the next preview. */
        }
    }
}
