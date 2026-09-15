using WTT.Campaigns.Server.Missions;

namespace WTT.Campaigns.Tests;

internal static class MissionLaunchContextChecks
{
    internal static void Run(Action<bool, string> check)
    {
        RunAsync(check).GetAwaiter().GetResult();
    }

    private static async Task RunAsync(Action<bool, string> check)
    {
        check(MissionLaunchContext.Current == null, "Ordinary requests begin without a mission marker");
        var scope = MissionLaunchContext.Push("character", true, "run");
        var resume = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Observe()
        {
            await resume.Task;
            check(
                MissionLaunchContext.Current?.RunId == "run" && MissionLaunchContext.Current.SessionId == "character",
                "The native asynchronous start retains its authenticated mission marker"
            );
        }
        var pending = Observe();
        scope.Restore();
        check(MissionLaunchContext.Current == null, "Returning from native dispatch releases the caller's mission marker");
        var ordinary = MissionLaunchContext.Push("other-character", false, "");
        check(MissionLaunchContext.Current?.HeaderPresent == false, "An overlapping ordinary launch has no mission authorization");
        ordinary.Restore();
        resume.SetResult(true);
        await pending;
        check(MissionLaunchContext.Current == null, "Completing a prior start cannot mark a subsequent ordinary request");
    }
}
