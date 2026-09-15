using WTT.Campaigns.Server.Profiles;

namespace WTT.Campaigns.Tests;

internal static class RaidFinalizationChecks
{
    private sealed class Lease : IDisposable { public bool Released; public void Dispose() => Released = true; }

    internal static void Run(Action<bool, string> check)
    {
        foreach (var failingStage in new[] { "story", "mission", "clear" })
        {
            var hub = false;
            var mission = false;
            var cleared = false;
            var nativeApplications = 0;
            var missionCompletions = 0;
            var fail = true;
            void Fault(string stage)
            {
                if (fail && failingStage == stage) { fail = false; throw new IOException("Injected " + stage + " failure"); }
            }
            var leases = new List<Lease>();
            Task Attempt()
            {
                var lease = new Lease();
                leases.Add(lease);
                if (RaidFinalization.RequiresNative(hub, mission)) nativeApplications++;
                return RaidFinalization.Complete(Task.CompletedTask, lease,
                    () => { hub = true; return Task.CompletedTask; },
                    () => { Fault("story"); return Task.CompletedTask; },
                    () => { Fault("mission"); if (!mission) { mission = true; missionCompletions++; } return Task.CompletedTask; },
                    () => { Fault("clear"); cleared = true; return Task.CompletedTask; });
            }
            try { Attempt().GetAwaiter().GetResult(); check(false, "Fault injection must fail initial finalization"); }
            catch (IOException) { check(leases.All(l => l.Released), "Finalization releases the character lease after " + failingStage + " failure"); }
            check(hub && !cleared, "Committed native/hub receipt survives " + failingStage + " failure");
            Attempt().GetAwaiter().GetResult();
            Attempt().GetAwaiter().GetResult();
            check(nativeApplications == 1, "Finalization retry does not repeat native inventory or experience after " + failingStage + " failure");
            check(missionCompletions == 1 && cleared && leases.All(l => l.Released), "Finalization recovers completion and raid cleanup once after " + failingStage + " failure");
        }
        check(RaidFinalization.RequiresNative(false, false), "Ordinary raid with no completion receipt uses native reconciliation");
        check(!RaidFinalization.RequiresNative(false, true), "Mission receipt independently prevents duplicate native reconciliation");
    }
}
