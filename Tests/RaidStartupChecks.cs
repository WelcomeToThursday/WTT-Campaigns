using Newtonsoft.Json;
using WTT.Campaigns.Client.Patches.Session;
using WTT.Campaigns.Server.Profiles;

namespace WTT.Campaigns.Tests;

internal static class RaidStartupChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        var settings = new Dictionary<string, object> { ["assault"] = new(), ["pmcbear"] = new() };
        var assault = settings["assault"];
        check(
            ReferenceEquals(settings[BotDifficultyFallback.Resolve("blackdivlead", settings)], assault),
            "Issue 5: missing blackdivlead loads fallback settings instead of throwing"
        );
        check(BotDifficultyFallback.Resolve("pmcbear", settings) == "pmcbear", "Existing bot difficulty settings remain authoritative");
        settings["blackdivlead"] = new();
        check(
            BotDifficultyFallback.Resolve("blackdivlead", settings) == "blackdivlead",
            "Mod-supplied Black Division difficulty is preserved"
        );
        check(BotDifficultyFallback.Resolve("futurebot", settings) == "assault", "Other client-only roles receive SPT's fallback");
        check(
            BotDifficultyFallback.Resolve("blackdivlead", new Dictionary<string, object>()) == "blackdivlead",
            "An empty cache does not invent difficulty data"
        );

        int aborted = 0;
        Exception? reported = null;
        Task Abort()
        {
            aborted++;
            return Task.CompletedTask;
        }
        await RaidLoadRecovery.Complete(Task.CompletedTask, Abort, error => reported = error);
        check(aborted == 0, "Successful raid loading keeps the character locked");
        var failure = new KeyNotFoundException("blackdivlead");
        try
        {
            await RaidLoadRecovery.Complete(Task.FromException(failure), Abort, error => reported = error);
            check(false, "Loading failure must reach EFT");
        }
        catch (KeyNotFoundException error)
        {
            check(
                ReferenceEquals(error, failure) && aborted == 1 && reported == null,
                "Failed load awaits one cleanup and preserves its exception"
            );
        }
        var networkError = new IOException("Server unavailable");
        try
        {
            await RaidLoadRecovery.Complete(Task.FromException(failure), () => Task.FromException(networkError), error => reported = error);
            check(false, "Cleanup failure must not swallow the raid failure");
        }
        catch (KeyNotFoundException error)
        {
            check(
                ReferenceEquals(error, failure) && ReferenceEquals(reported, networkError),
                "Cleanup failure is reported without hiding the original raid failure"
            );
        }
        try
        {
            await RaidLoadRecovery.Complete(Task.FromCanceled(new CancellationToken(true)), Abort, error => reported = error);
            check(false, "Cancellation must reach EFT");
        }
        catch (OperationCanceledException)
        {
            check(aborted == 2, "Cancelled loading also releases its raid state");
        }
        var cleanup = new TaskCompletionSource();
        var pending = RaidLoadRecovery.Complete(Task.FromException(failure), () => cleanup.Task, _ => { });
        check(!pending.IsCompleted, "EFT cannot return to the menu before cleanup finishes");
        cleanup.SetResult();
        try
        {
            await pending;
            check(false, "Cleanup completion must preserve the raid failure");
        }
        catch (KeyNotFoundException) { }

        var link = JsonConvert.DeserializeObject<AccountLink>("{\"ActiveRaidProfiles\":[\"character\"]}")!;
        check(
            !RaidAbortGuard.Matches(link, "character", "character", "raid-one"),
            "Older account links migrate without accepting an untracked abort"
        );
        link.ActiveRaidIds["character"] = "raid-one";
        check(
            RaidAbortGuard.Matches(link, "character", "character", "raid-one"),
            "Matching character and raid can release a failed startup"
        );
        check(!RaidAbortGuard.Matches(link, "other-character", "character", "raid-one"), "An abort cannot clear another active character");
        check(!RaidAbortGuard.Matches(link, "character", "character", "previous-raid"), "A stale abort cannot clear a newer raid");
        check(!RaidAbortGuard.Matches(link, "character", "character", ""), "Missing raid identity cannot clear a lock");
        link.ActiveRaidProfiles.Clear();
        check(!RaidAbortGuard.Matches(link, "character", "character", "raid-one"), "Repeated aborts and finished raids are ignored");
    }
}
