using WTT.Campaigns.Client.Profiles;

namespace WTT.Campaigns.Tests;

internal static class ProfileSaveChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        check(ProfileSaveGate.IsEmpty(true, false, 0, 0, false), "An idle empty native queue requires no flush callbacks");
        check(!ProfileSaveGate.IsEmpty(false, false, 0, 0, false), "An awaiting or failed queue is not saved");
        check(!ProfileSaveGate.IsEmpty(true, true, 0, 0, false), "An active flush cannot be skipped");
        check(!ProfileSaveGate.IsEmpty(true, false, 1, 0, false), "Incoming inventory operations must be saved");
        check(!ProfileSaveGate.IsEmpty(true, false, 0, 1, false), "Idle unsent inventory operations must be saved");
        check(!ProfileSaveGate.IsEmpty(true, false, 0, 0, true), "A waiting operation must not be skipped");
        var gate = new ProfileSaveGate();
        var owner = new object();
        var saves = 0;
        Task Save() { saves++; return Task.CompletedTask; }
        Task Never(CancellationToken token) => Task.Delay(Timeout.Infinite, token);
        await gate.Run(owner, () => true, Save, Never);
        check(saves == 0, "Empty menu entry bypasses the native flush entirely");
        await gate.Run(owner, () => false, Save, Never);
        check(saves == 1, "Pending changes invoke and await the native save");

        var pending = new TaskCompletionSource();
        var expired = new TaskCompletionSource();
        var continued = false;
        async Task Enter()
        {
            await gate.Run(owner, () => false, () => { saves++; return pending.Task; }, _ => expired.Task);
            continued = true;
        }
        var entry = Enter();
        expired.SetResult();
        try { await entry; check(false, "Save deadline must abort entry"); }
        catch (TimeoutException) { check(!continued, "A stuck save cannot switch profile identity"); }
        var retry = gate.Run(owner, () => true, Save, Never);
        check(!retry.IsCompleted && saves == 2, "Retry waits for the same native save even if queue now looks empty");
        pending.SetResult();
        await retry;
        check(!continued, "Late save completion cannot resume an expired editor entry");
        try
        {
            await gate.Run(owner, () => false, () => Task.FromException(new InvalidOperationException("save failed")), Never);
            check(false, "Native save failure must propagate");
        }
        catch (InvalidOperationException e) { check(e.Message == "save failed", "Native save failure blocks the transition"); }
    }
}
