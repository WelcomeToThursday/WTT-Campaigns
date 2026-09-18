using WTT.Campaigns.Client.Authoring.Preview;

namespace WTT.Campaigns.Tests;

internal static class PreviewLoadGuardChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        static TaskCompletionSource<bool> Pending() => new(TaskCreationOptions.RunContinuationsAsynchronously);
        var never = Pending();
        var timerCancelled = false;
        await PreviewLoadGuard.Run(
            _ => Task.CompletedTask,
            token =>
            {
                token.Register(() => timerCancelled = true);
                return never.Task;
            },
            CancellationToken.None,
            () => "unexpected"
        );
        check(timerCancelled, "Completing equipment loading retires its deadline");
        never.TrySetResult(true);

        var native = Pending();
        var nativeCancelled = false;
        var timedOut = false;
        try
        {
            await PreviewLoadGuard.Run(
                token =>
                {
                    token.Register(() => nativeCancelled = true);
                    return native.Task;
                },
                _ => Task.CompletedTask,
                CancellationToken.None,
                () => "equipment bundles stalled"
            );
        }
        catch (TimeoutException error)
        {
            timedOut = error.Message == "equipment bundles stalled";
        }
        check(timedOut && nativeCancelled, "A hung native equipment loader returns a stage-specific timeout and cancels native work");
        native.SetException(new InvalidOperationException("Late native failure"));

        using var lifetime = new CancellationTokenSource();
        var deadline = Pending();
        var loading = Pending();
        var guarded = PreviewLoadGuard.Run(_ => loading.Task, _ => deadline.Task, lifetime.Token, () => "not a timeout");
        lifetime.Cancel();
        var cancelled = false;
        try
        {
            await guarded;
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        check(cancelled, "Closing the playtest cancels a wait even when its native loader ignores cancellation");
        loading.TrySetResult(true);
        deadline.TrySetResult(true);

        var failure = false;
        try
        {
            await PreviewLoadGuard.Run(
                _ => Task.FromException(new InvalidOperationException("missing model")),
                _ => Task.CompletedTask,
                CancellationToken.None,
                () => "wrong error"
            );
        }
        catch (InvalidOperationException error)
        {
            failure = error.Message == "missing model";
        }
        check(failure, "Native equipment failures are preserved instead of being reported as generic timeouts");

        var synchronousDeadlineCancelled = false;
        var synchronousFailure = false;
        var unusedDeadline = Pending();
        try
        {
            await PreviewLoadGuard.Run(
                _ => throw new InvalidOperationException("synchronous native failure"),
                token =>
                {
                    token.Register(() => synchronousDeadlineCancelled = true);
                    return unusedDeadline.Task;
                },
                CancellationToken.None,
                () => "wrong error"
            );
        }
        catch (InvalidOperationException error)
        {
            synchronousFailure = error.Message == "synchronous native failure";
        }
        check(synchronousFailure && synchronousDeadlineCancelled, "Synchronous native failures retire the already-running watchdog");
        unusedDeadline.TrySetResult(true);

        var waiting = Pending();
        var diagnosticFailure = false;
        try
        {
            await PreviewLoadGuard.Run(
                _ => waiting.Task,
                _ => Task.FromException(new InvalidOperationException("failed dependency.bundle")),
                CancellationToken.None,
                () => "wrong timeout"
            );
        }
        catch (InvalidOperationException error)
        {
            diagnosticFailure = error.Message == "failed dependency.bundle";
        }
        check(diagnosticFailure, "The watchdog reports a failed bundle without replacing it with a generic timeout");
        waiting.TrySetResult(true);
    }
}
