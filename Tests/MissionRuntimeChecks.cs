using Newtonsoft.Json.Linq;
using WTT.Campaigns.Client.Missions;
using WTT.Campaigns.Shared.Missions;
using WTT.Campaigns.Shared.Native;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Tests;

internal static class MissionRuntimeChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var checkpoints = new[] { new MapVolume { Id = "first" }, new MapVolume { Id = "second" } };
        var exit = new MapVolume { Id = "exit", Name = "Test exit" };
        var run = new MissionRun { CharacterId = "character", RunId = "run", RaidId = "raid", EncounterToken = "token", Status = MissionRunStatuses.Active };
        check(MissionRunRules.MatchesIdentity(run, "character", "run", "raid", "token"), "Mission identity accepts its exact run and encounter token");
        foreach (var invalid in new[] { "", "different" })
        {
            check(!MissionRunRules.MatchesIdentity(run, invalid, "run", "raid", "token"), "Mission identity rejects wrong/empty character");
            check(!MissionRunRules.MatchesIdentity(run, "character", invalid, "raid", "token"), "Mission identity rejects wrong/empty run");
            check(!MissionRunRules.MatchesIdentity(run, "character", "run", invalid, "token"), "Mission identity rejects wrong/empty raid");
            check(!MissionRunRules.MatchesIdentity(run, "character", "run", "raid", invalid), "Mission identity rejects wrong/empty encounter token");
        }
        check(!MissionRunRules.TryCheckpoint(run, checkpoints, "second", out _) && run.NextCheckpointIndex == 0, "Mission checkpoints cannot be skipped");
        check(!MissionRunRules.TryExit(run, checkpoints, exit, "exit", out _) && !run.ExitReached, "Mission exit rejects an incomplete route");
        check(MissionRunRules.TryCheckpoint(run, checkpoints, "first", out _) && run.NextCheckpointIndex == 1, "First checkpoint advances once");
        check(MissionRunRules.TryCheckpoint(run, checkpoints, "first", out _) && run.NextCheckpointIndex == 1, "Repeated checkpoint is an idempotent no-op");
        check(!MissionRunRules.TryCheckpoint(run, checkpoints, "unknown", out _) && run.NextCheckpointIndex == 1, "Unknown checkpoint cannot advance progress");
        check(MissionRunRules.TryCheckpoint(run, checkpoints, "second", out _) && run.NextCheckpointIndex == 2, "Second checkpoint completes the route");
        check(!MissionRunRules.TryExit(run, checkpoints, exit, "normal-extract", out _) && !run.ExitReached, "Only the authored exit can be recorded");
        check(!MissionRunRules.IsSuccessfulExtraction(run, checkpoints, exit, "Survived", exit.Name, out _), "Native extraction alone cannot complete a mission");
        check(MissionRunRules.TryExit(run, checkpoints, exit, "exit", out _), "Authored exit opens after every checkpoint");
        check(MissionRunRules.TryExit(run, checkpoints, exit, "exit", out _) && run.ExitReached, "Repeated exit report is idempotent");
        foreach (var failure in new[] { "Killed", "MissingInAction", "Left", "Disconnected", "", "unknown" })
            check(!MissionRunRules.IsSuccessfulExtraction(run, checkpoints, exit, failure, exit.Name, out _), "Non-survival result never completes mission: " + failure);
        check(!MissionRunRules.IsSuccessfulExtraction(run, checkpoints, exit, "Survived", "normal-extract", out _), "Wrong native exit name cannot finalize a mission");
        check(MissionRunRules.IsSuccessfulExtraction(run, checkpoints, exit, "Survived", exit.Name, out _), "Alive authored extraction completes mission");
        var retry = new MissionRun { CharacterId = "character", RunId = "retry", RaidId = "new-raid", Status = MissionRunStatuses.Active };
        check(retry.NextCheckpointIndex == 0 && !retry.ExitReached && retry.CompletedCheckpointIds.Count == 0, "Retry starts without prior route state");
        check(!MissionRunRules.MatchesIdentity(retry, "character", run.RunId, run.RaidId), "Old callbacks cannot address a retry");

        check(
            !MissionProgressCallbackGuard.IsCurrent(
                active: true,
                ending: false,
                lifetimeCancelled: false,
                sameLifetime: true,
                inRaid: true,
                samePlayer: true,
                sameWorld: true,
                sameDescriptor: true,
                currentGeneration: 2,
                capturedGeneration: 1,
                currentRunId: "new-run",
                capturedRunId: "old-run",
                currentRaidId: "new-raid",
                capturedRaidId: "old-raid",
                currentMissionId: "mission",
                capturedMissionId: "mission"
            ),
            "A delayed progress callback from the previous mission run is discarded"
        );
        check(
            MissionProgressCallbackGuard.IsCurrent(
                active: true,
                ending: false,
                lifetimeCancelled: false,
                sameLifetime: true,
                inRaid: true,
                samePlayer: true,
                sameWorld: true,
                sameDescriptor: true,
                currentGeneration: 2,
                capturedGeneration: 2,
                currentRunId: "run",
                capturedRunId: "run",
                currentRaidId: "raid",
                capturedRaidId: "raid",
                currentMissionId: "mission",
                capturedMissionId: "mission"
            ),
            "The current mission progress callback is accepted"
        );

        var source = new List<NativeItem>
        {
            new() { Id = "100000000000000000000001", Template = "5447a9cd4bdc2dbd208b4567" },
            new() { Id = "100000000000000000000002", Template = "55818b164bdc2ddc698b456c", ParentId = "100000000000000000000001", SlotId = "mod_scope" },
        };
        var before = JToken.FromObject(source);
        var first = MissionLootRecords.CopyForRun(source);
        var second = MissionLootRecords.CopyForRun(source);
        check(JToken.DeepEquals(before, JToken.FromObject(source)), "Native mission loot preparation preserves authored items");
        check(first.Select(i => i.Id).Intersect(source.Select(i => i.Id)).Count() == 0, "Native mission loot uses fresh item identities");
        check(first.Select(i => i.Id).Intersect(second.Select(i => i.Id)).Count() == 0, "Replays cannot reuse extracted loot identities");
        check(first[1].ParentId == first[0].Id && first[1].SlotId == source[1].SlotId, "Native mission loot preserves assembly links after identity remap");
        check(first.Select(i => i.Template).SequenceEqual(source.Select(i => i.Template)), "Native mission loot preserves templates");
    }
}
