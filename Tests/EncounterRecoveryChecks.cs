using WTT.Campaigns.Client.Encounters;
using WTT.Campaigns.Shared.Missions;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Tests;

internal static class EncounterRecoveryChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        var calls = 0;
        var delays = new List<int>();
        var retries = new List<int>();
        Task Delay(int milliseconds, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            delays.Add(milliseconds);
            return Task.CompletedTask;
        }
        var result = await EncounterRecovery.RequestAsync(
            () => ++calls < 3 ? Task.FromException<string>(new TimeoutException()) : Task.FromResult("same chunk"),
            CancellationToken.None,
            retries.Add,
            Delay
        );
        check(
            result == "same chunk" && calls == 3 && delays.SequenceEqual(new[] { 500, 1000 }) && retries.SequenceEqual(new[] { 2, 3 }),
            "Profile transport recovers with bounded backoff before activating any bot"
        );
        calls = 0;
        try
        {
            await EncounterRecovery.RequestAsync<string>(
                () =>
                {
                    calls++;
                    throw new TimeoutException();
                },
                CancellationToken.None,
                _ => { },
                Delay
            );
            check(false, "Transport exhaustion must fail");
        }
        catch (TimeoutException)
        {
            check(calls == 3, "Transport exhaustion stops after exactly three attempts");
        }
        foreach (
            var error in new Exception[]
            {
                new InvalidOperationException("Rejected identity"),
                new System.Net.Http.HttpRequestException("Unauthorized"),
                new Newtonsoft.Json.JsonSerializationException("Invalid response"),
            }
        )
        {
            calls = 0;
            try
            {
                await EncounterRecovery.RequestAsync<string>(
                    () =>
                    {
                        calls++;
                        throw error;
                    },
                    CancellationToken.None,
                    _ => { },
                    Delay
                );
                check(false, "Permanent failure must propagate");
            }
            catch (Exception actual)
            {
                check(ReferenceEquals(error, actual) && calls == 1, "Validation/authentication/serialization errors are not retried");
            }
        }
        using var cancelled = new CancellationTokenSource();
        calls = 0;
        try
        {
            await EncounterRecovery.RequestAsync<string>(
                () =>
                {
                    calls++;
                    throw new TimeoutException();
                },
                cancelled.Token,
                _ => cancelled.Cancel(),
                Delay
            );
            check(false, "Cancelled recovery must stop");
        }
        catch (OperationCanceledException)
        {
            check(calls == 1, "Reset during backoff cannot request or activate another actor");
        }
        using var late = new CancellationTokenSource();
        try
        {
            await EncounterRecovery.RequestAsync(
                () =>
                {
                    late.Cancel();
                    return Task.FromResult("late profile");
                },
                late.Token,
                _ => { },
                Delay
            );
            check(false, "Late profile response cannot enter activation");
        }
        catch (OperationCanceledException)
        {
            check(true, "A retired generation discards a successful late profile response");
        }

        var cleanup = new EncounterWaveCleanup();
        var removed = new List<string>();
        cleanup.Track(() => removed.Add("first native bot"));
        cleanup.Track(() =>
        {
            removed.Add("first AI binding");
            throw new InvalidOperationException("Cleanup locked");
        });
        cleanup.Track(() => removed.Add("failed native activation"));
        try
        {
            cleanup.Rollback();
            check(false, "Incomplete cleanup must remain visible");
        }
        catch (AggregateException error)
        {
            check(
                error.InnerExceptions.Count == 1
                    && removed.SequenceEqual(new[] { "failed native activation", "first AI binding", "first native bot" }),
                "A partial-wave rollback removes every tracked activation even when an AI cleanup fails"
            );
        }
        cleanup.Rollback();
        check(removed.Count == 3, "Completed rollback does not dispose actors twice");
        cleanup.Track(() => throw new Exception("Committed actor removed"));
        cleanup.Commit();
        cleanup.Rollback();
        check(true, "Successfully committed waves retain their actors");

        var layout = new MapLayout
        {
            Checkpoints = new() { new() { Id = "cp" } },
            Exit = new() { Id = "exit", Name = "Exit" },
        };
        var run = new MissionRun
        {
            RunId = "run",
            RaidId = "raid",
            Status = MissionRunStatuses.Active,
        };
        var checkpoint = new MissionCheckpoint(run, "");
        var refreshed = Newtonsoft.Json.JsonConvert.DeserializeObject<MissionRun>(Newtonsoft.Json.JsonConvert.SerializeObject(run))!;
        MissionAcknowledgement.RequireCurrent(run, refreshed);
        try
        {
            MissionAcknowledgement.Require(run, refreshed, committed: false);
            check(false, "Read cannot acknowledge a mutation");
        }
        catch (InvalidOperationException)
        {
            check(true, "Technical recovery refresh preserves committed-response enforcement");
        }
        refreshed.AttemptGeneration++;
        try
        {
            MissionAcknowledgement.RequireCurrent(run, refreshed);
            check(false, "Refresh cannot adopt another attempt");
        }
        catch (InvalidOperationException)
        {
            check(true, "Recovery revision refresh rejects a retired or replacement attempt");
        }
        MissionRunRules.InterruptEncounter(run);
        MissionRunRules.InterruptEncounter(run);
        foreach (
            var rejected in new[]
            {
                new MissionRun { Status = MissionRunStatuses.Prepared },
                new MissionRun { Status = MissionRunStatuses.Active, Restoring = true },
                new MissionRun { Status = MissionRunStatuses.Active, ExitReached = true },
                new MissionRun { Status = MissionRunStatuses.Succeeded },
            }
        )
        {
            try
            {
                MissionRunRules.InterruptEncounter(rejected);
                check(false, "Invalid interruption must be rejected");
            }
            catch (InvalidOperationException)
            {
                check(
                    !rejected.TechnicalFailure,
                    "Technical interruption cannot mutate a prepared, restoring, extracting, or completed mission"
                );
            }
        }
        check(
            !run.PlayerDefeated && run.Logic.Failure.Length == 0,
            "Technical interruption is separate from player defeat and objective failure"
        );
        check(!MissionRunRules.TryCheckpoint(run, layout.Checkpoints, "cp", out _), "Interrupted missions cannot advance checkpoints");
        run.NextCheckpointIndex = 1;
        check(!MissionRunRules.TryExit(run, layout.Checkpoints, layout.Exit, "exit", out _), "Interrupted missions cannot accept an exit");
        run.ExitReached = true;
        check(
            !MissionRunRules.IsSuccessfulExtraction(run, layout.Checkpoints, layout.Exit, "survived", "Exit", out var reason)
                && reason.Contains("technical"),
            "Ending a technical interruption alive cannot grant mission completion"
        );
        run.ExitReached = false;
        try
        {
            _ = new MissionCheckpoint(run, "");
            check(false, "Interrupted checkpoint must be rejected");
        }
        catch (InvalidOperationException)
        {
            check(true, "A technical interruption cannot overwrite a healthy checkpoint");
        }
        try
        {
            MissionObservationRules.Apply(
                new(),
                layout,
                run,
                new()
                {
                    AttemptGeneration = 1,
                    Signals = new() { new() { Kind = MissionSignals.Tick } },
                },
                10
            );
            check(false, "Interrupted observations must be rejected");
        }
        catch (InvalidOperationException)
        {
            check(true, "Technical failure freezes server observations");
        }
        var restored = checkpoint.BeginRestore(run);
        check(
            restored.AttemptGeneration == 2
                && restored.Restoring
                && !restored.TechnicalFailure
                && !restored.PlayerDefeated
                && restored.EncounterToken != run.EncounterToken,
            "Technical checkpoint retry retires old identities and restores healthy mission state"
        );
        checkpoint.CommitRestore(new(), layout, restored, new Dictionary<string, string>());
        check(
            MissionRunRules.TryCheckpoint(restored, layout.Checkpoints, "cp", out _),
            "A committed technical retry can progress normally"
        );
    }
}
