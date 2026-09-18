using Newtonsoft.Json;
using WTT.Campaigns.Shared.Missions;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Tests;

internal static class MissionCheckpointChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var testLayout = new MapLayout { Id = "unlinked-layout", Name = "Route rehearsal" };
        var rehearsal = WTT.Campaigns.Shared.Authoring.EditorCheckpointTest.Definition(testLayout);
        check(rehearsal.LayoutId == testLayout.Id && rehearsal.CheckpointRetries && rehearsal.Objectives.Count == 0
            && rehearsal.Events.Count == 0 && rehearsal.Requirements.Count == 0 && rehearsal.QuestId.Length == 0,
            "Layout checkpoint rehearsal needs no linked mission and enables retries without objectives or quest rewards");
        check(testLayout.Id == "unlinked-layout" && testLayout.Name == "Route rehearsal",
            "Checkpoint rehearsal preserves the authored layout");
        var layout = new MapLayout { Checkpoints = [new() { Id = "cp" }], Exit = new() { Id = "exit", Name = "Exit" } };
        var mission = new MissionDefinition
        {
            Events = [new() { Id = "start", Source = MissionSignals.Start, Actions = [new() { Type = MissionAction.Timer, TargetId = "clock", Seconds = 10 }] },
                new() { Id = "checkpoint", Source = MissionSignals.Checkpoint, SourceId = "cp",
                    Actions = [new() { Type = MissionAction.Timer, TargetId = "after", Seconds = 5 }] }],
        };
        var run = new MissionRun { RunId = "run", RaidId = "raid", Status = MissionRunStatuses.Active };
        var start = new MissionCheckpoint(run, "");
        MissionLogic.Apply(mission, layout, run.Logic, new() { Kind = MissionSignals.Start });
        MissionLogic.Apply(mission, layout, run.Logic, new() { Kind = MissionSignals.Tick, Time = 4 });
        run.Logic.Failure = "Player defeated";
        var restored = start.BeginRestore(run);
        check(restored.AttemptGeneration == 2 && restored.Restoring && restored.Logic.Time == 0 && restored.Logic.Failure.Length == 0,
            "Retry before first checkpoint rewinds to mission start and retires the attempt");
        check(!MissionRunRules.TryCheckpoint(restored, layout.Checkpoints, "cp", out _), "Restore cannot advance checkpoint");
        Throws(() => MissionObservationRules.Apply(mission, layout, restored,
            new() { AttemptGeneration = 2, Signals = [new() { Kind = MissionSignals.Tick }] }, 10), "Restore cannot accept observations");
        Throws(() => start.BeginRestore(restored), "A partial restoration cannot begin a second restoration");
        start.CommitRestore(mission, layout, restored, new Dictionary<string, string>());
        check(!restored.Restoring && restored.Logic.FiredRules.SetEquals(["start"]) && restored.Logic.Timers["clock"] == 10,
            "Committed start retry replays mission-start actions once");
        Throws(() => start.CommitRestore(mission, layout, restored, new Dictionary<string, string>()), "Restore commit cannot dispatch actions twice");
        for (var generation = 3; generation <= 6; generation++)
        {
            restored.Logic.Time = 8;
            restored = start.BeginRestore(restored);
            start.CommitRestore(mission, layout, restored, new Dictionary<string, string>());
            check(restored.AttemptGeneration == generation && restored.Logic.Time == 0 && restored.Logic.Timers["clock"] == 10,
                "Repeated retries preserve monotonic generation and immutable start clock");
        }
        MissionLogic.Apply(mission, layout, restored.Logic, new() { Kind = MissionSignals.Tick, Time = 3 });
        restored.Logic.Actors.Add("living", new() { ProfileId = "living", Spawned = true, RosterId = "r" });
        restored.Logic.Actors.Add("dead", new() { ProfileId = "dead", Spawned = true, Dead = true, RosterId = "r" });
        restored.Logic.Actors.Add("pending", new() { ProfileId = "pending", RosterId = "r" });
        restored.Logic.Zones["cp"] = new() { TargetId = "cp", Occupants = ["living"] };
        check(MissionRunRules.TryCheckpoint(restored, layout.Checkpoints, "cp", out _), "Accepted checkpoint records its identity");
        var checkpoint = new MissionCheckpoint(restored, "cp");
        MissionLogic.Apply(mission, layout, restored.Logic, new() { Kind = MissionSignals.Checkpoint, TargetId = "cp", Time = 3 });
        restored.Logic.Actors["living"].Dead = true;
        restored.Logic.Time = 30;
        var retry = checkpoint.BeginRestore(restored);
        MissionAcknowledgement.RequireRestore(restored,
            JsonConvert.DeserializeObject<MissionRun>(JsonConvert.SerializeObject(retry)), true, preparing: true);
        check(!restored.ExitReached && retry.AttemptGeneration == restored.AttemptGeneration + 1,
            "Unfinished route checkpoint retry survives the response JSON boundary and acknowledges the next attempt");
        check(retry.CheckpointId == "cp" && retry.NextCheckpointIndex == 1 && !retry.Logic.FiredRules.Contains("checkpoint"),
            "Checkpoint captures route acceptance before checkpoint actions");
        check(!retry.Logic.Actors["living"].Dead && retry.Logic.Actors["dead"].Dead && retry.Logic.Time == 3,
            "Snapshot is isolated from abandoned deaths and elapsed timers");
        var before = JsonConvert.SerializeObject(retry);
        Throws(() => checkpoint.CommitRestore(mission, layout, retry, new Dictionary<string, string>()), "Missing survivor blocks commit");
        check(JsonConvert.SerializeObject(retry) == before && retry.Restoring, "Failed restore validation cannot mutate or release the run");
        Throws(() => checkpoint.CommitRestore(mission, layout, retry, new Dictionary<string, string> { ["living"] = "dead" }),
            "Fresh survivor cannot reuse a checkpoint actor identity");
        check(!MissionRunRules.TryExit(retry, layout.Checkpoints, layout.Exit, "exit", out _), "Partial restore cannot extract");
        retry.ExitReached = true;
        check(!MissionRunRules.IsSuccessfulExtraction(retry, layout.Checkpoints, layout.Exit, "Survived", "Exit", out _),
            "Partial restore cannot earn native extraction completion");
        retry.ExitReached = false;
        checkpoint.CommitRestore(mission, layout, retry, new Dictionary<string, string> { ["living"] = "fresh" });
        check(retry.Logic.Actors.ContainsKey("fresh") && !retry.Logic.Actors.ContainsKey("living") && !retry.Logic.Actors.ContainsKey("pending")
            && retry.Logic.Actors["dead"].Dead && retry.Logic.Zones["cp"].Occupants.SequenceEqual(["fresh"]),
            "Restore remaps living actors and occupancy, preserves confirmed deaths, and discards unfinished generation");
        check(retry.Logic.Timers["after"] == 8 && retry.Logic.Timers["clock"] == 10, "Checkpoint actions replay with restored timer deadlines");
        Throws(() => checkpoint.BeginRestore(new() { RunId = "other", RaidId = "raid", Status = MissionRunStatuses.Active }),
            "Checkpoint cannot restore another run");
        var progress = new MissionProgress { ActiveRun = retry };
        MissionTransaction.AddReceipt(progress, "op", "input", "progress", retry, 1);
        check(MissionTransaction.TryReplayReceipt(progress, "op", "input", out _), "Same-attempt lost response remains replayable");
        progress.ActiveRun = checkpoint.BeginRestore(retry);
        Throws(() => MissionTransaction.TryReplayReceipt(progress, "op", "input", out _), "Retired-attempt receipt cannot acknowledge a restored attempt");
        MissionAcknowledgement.Require(retry, JsonConvert.DeserializeObject<MissionRun>(JsonConvert.SerializeObject(retry)), true);
        Throws(() => MissionAcknowledgement.Require(retry, retry, false), "Uncommitted observation response cannot clear pending observations");
        Throws(() => MissionAcknowledgement.Require(retry, progress.ActiveRun, true), "Different-attempt response cannot dispatch actions");
        Throws(() => MissionAcknowledgement.Require(retry, null, true), "Missing response run cannot clear pending observations");

        var encounter = new MapEncounter { Id = "enc", Waves = [new() { Id = "wave", DelaySeconds = 2,
            Roster = [new() { Id = "r", Count = 1, SpawnPointIds = ["point"] }] }, new() { Id = "next", DelaySeconds = 5, WaitForPreviousWave = true }] };
        var waves = new EncounterWaveStateMachine(encounter);
        waves.TryActivate("start", 10);
        var delayed = waves.Capture(11);
        var newWaves = new EncounterWaveStateMachine(encounter);
        newWaves.Restore(delayed, 100, new Dictionary<string, string>());
        check(newWaves.ReadyWaves(100).Count == 0 && newWaves.ReadyWaves(101).SequenceEqual([0]), "Wave delay keeps its remaining duration after rewind");
        newWaves.TryBeginGeneration(0, "old-generation");
        Throws(() => newWaves.Capture(101), "Checkpoint capture waits for pending native generation");
        newWaves.TryReserveBot(0, "old-generation", "r", "old-bot", "point", out _);
        newWaves.TryCommitGeneration(0, "old-generation", 101);
        var aliveWave = newWaves.Capture(102);
        newWaves.Cancel();
        newWaves.Restore(aliveWave, 200, new Dictionary<string, string> { ["old-bot"] = "new-bot" });
        check(!newWaves.TryCommitGeneration(0, "old-generation", 201), "Abandoned generation cannot commit into a restored wave");
        check(!newWaves.MarkBotDefeated(0, "old-bot", 201), "Abandoned bot death cannot finish a restored wave");
        check(newWaves.MarkBotDefeated(0, "new-bot", 201), "Recreated survivor remains assigned to its authored wave");
        var completed = newWaves.Capture(202);
        newWaves.Restore(completed, 300, new Dictionary<string, string>());
        check(newWaves.ReadyWaves(303).Count == 0 && newWaves.ReadyWaves(304).SequenceEqual([1]),
            "Death-gated next wave retains its remaining post-completion delay");

        var admission = new EncounterSpawnAdmission();
        var patrol = new EncounterPatrolStateMachine(new() { Id = "patrol", Waypoints = [new(), new()] });
        patrol.Restore(new() { RouteId = "patrol", Waypoint = 1, Direction = -1, WaitRemaining = 4 }, 100);
        var patrolPoint = patrol.Capture(102);
        patrol.Restore(patrolPoint, 500);
        check(patrol.TargetWaypointIndex == 1 && patrol.Status == PatrolRuntimeStatus.Waiting
            && patrol.Capture(501).WaitRemaining == 1 && patrol.Capture(501).Direction == -1,
            "Checkpoint patrol restores waypoint, traversal direction and remaining wait against the new clock");
        Throws(() => patrol.Restore(new() { RouteId = "other" }, 0), "Checkpoint cannot restore a different patrol route");
        var context = new EncounterRuntimeContext { SessionId = "s", RaidId = "r", LayoutId = "l", PreviewGeneration = "run" };
        admission.TryReserve(context, "enc", "bot", "point", out var reservation);
        context.AttemptGeneration++;
        check(!admission.TryConsume(context, reservation!, out _), "Native spawn admission rejects an earlier attempt reservation");

        void Throws(Action action, string message)
        {
            try { action(); check(false, message); }
            catch (InvalidOperationException) { check(true, message); }
        }
    }
}
