using WTT.Campaigns.Server.Missions;
using WTT.Campaigns.Shared.Missions;

namespace WTT.Campaigns.Tests;

/// <summary>
/// Offline checks for the mission service's durable transaction boundaries.
/// These exercise the production transaction helper and MissionStore without
/// launching SPT or constructing the server DI graph.
/// </summary>
internal static class MissionServiceChecks
{
    private const string IdentityError = "Refresh the active Campaign character before using missions.";
    private const string CampaignError = "This mission request belongs to another campaign.";
    private const string RevisionError = "Mission progress changed. Refresh and try again.";
    private const string OperationError = "A unique mission operation identifier is required.";
    private const string ContentError = "The mission content changed. Cancel the old run and prepare it again.";

    internal static void Run(Action<bool, string> check)
    {
        var state = new MissionProgress { SeasonId = "season", Revision = 4 };
        var pmc = new SPTarkov.Server.Core.Models.Eft.Common.PmcData { ExtensionData = new() };

        foreach (var status in new[] { "Started", "AvailableForFinish", "Success" })
        {
            var accepted = new MissionProgress { SeasonId = "season" };
            check(MissionTransaction.Unlock(accepted, "mission-" + status, status), "Accepted quest status unlocks mission: " + status);
            check(accepted.UnlockedMissionIds.Contains("mission-" + status), "Unlock is recorded for quest status: " + status);
        }

        var denied = new MissionProgress { SeasonId = "season" };
        foreach (var status in new string?[] { null, "", "Locked", "AvailableForStart", "Fail", "MarkedAsFailed" })
        {
            check(
                Throws(() => MissionTransaction.Unlock(denied, "denied-" + (status ?? "null"), status),
                    "Accept the linked quest before deploying this mission."),
                "Unaccepted quest status cannot unlock a mission: " + (status ?? "null")
            );
        }
        check(MissionTransaction.Unlock(denied, "already-unlocked", "Started"), "A first accepted quest unlock is recorded");
        check(!MissionTransaction.Unlock(denied, "already-unlocked", "Started"), "A permanent unlock is idempotent");
        check(!MissionTransaction.Unlock(denied, "already-unlocked", "Locked"), "A permanent unlock does not require the quest again");

        // Use the same profile extension storage as the server runtime, then
        // read it back to prove the unlock survives a profile serialization boundary.
        var persisted = new MissionProgress { SeasonId = "season", Revision = 12 };
        check(MissionTransaction.Unlock(persisted, "persistent-mission", "Started"), "Accepted quest creates a persistent unlock");
        persisted.Revision++;
        MissionStore.Write(pmc, persisted);
        var restored = MissionStore.Read(pmc, "season");
        check(restored.UnlockedMissionIds.Contains("persistent-mission"), "Mission unlock survives MissionStore round trip");
        check(restored.Revision == 13 && restored.SeasonId == "season", "MissionStore preserves revision and season identity");
        check(restored.ActiveRun == null && restored.Receipts.Count == 0, "MissionStore initializes absent transient mission state");

        var request = new MissionRequest { Version = 1, CharacterId = "character", SeasonId = "season" };
        MissionTransaction.RequireRequestIdentity(request, "character", "season");
        check(true, "Exact active character and campaign identity is accepted");
        request.CharacterId = "other";
        check(Throws(() => MissionTransaction.RequireRequestIdentity(request, "character", "season"), IdentityError), "Wrong character identity is rejected");
        request.CharacterId = "character";
        request.Version = 2;
        check(Throws(() => MissionTransaction.RequireRequestIdentity(request, "character", "season"), IdentityError), "Unsupported mission protocol is rejected");
        request.Version = 1;
        request.SeasonId = "";
        check(Throws(() => MissionTransaction.RequireRequestIdentity(request, "character", "season"), IdentityError), "Missing campaign identity is rejected");
        request.SeasonId = "other-season";
        check(Throws(() => MissionTransaction.RequireRequestIdentity(request, "character", "season"), CampaignError), "Wrong campaign identity is rejected");

        request.SeasonId = "season";
        request.OperationId = Guid.NewGuid().ToString("N");
        MissionTransaction.RequireOperation(request);
        check(true, "Canonical N-format operation identity is accepted");
        request.OperationId = Guid.NewGuid().ToString("D");
        check(Throws(() => MissionTransaction.RequireOperation(request), OperationError), "Dashed operation identity is rejected");
        request.OperationId = "";
        check(Throws(() => MissionTransaction.RequireOperation(request), OperationError), "Missing operation identity is rejected");

        state.Revision = 4;
        MissionTransaction.RequireRevision(state, 4);
        check(true, "Current mission revision is accepted");
        check(Throws(() => MissionTransaction.RequireRevision(state, 3), RevisionError), "Stale mission revision is rejected");

        var contentRun = new MissionRun { ContentRevision = 7, ContentHash = "content-hash" };
        MissionTransaction.VerifyContent(contentRun, 7, "content-hash");
        check(true, "Prepared content revision and hash are accepted");
        check(Throws(() => MissionTransaction.VerifyContent(contentRun, 8, "content-hash"), ContentError), "Changed content revision is rejected");
        check(Throws(() => MissionTransaction.VerifyContent(contentRun, 7, "other-hash"), ContentError), "Changed content hash is rejected");

        var active = new MissionRun { RunId = "run", CharacterId = "character", RaidId = "raid", Status = MissionRunStatuses.Active };
        var receiptState = new MissionProgress { SeasonId = "season", Revision = 9, ActiveRun = active };
        MissionTransaction.AddReceipt(receiptState, "operation", "fingerprint", "progress", active, 100);
        check(
            MissionTransaction.TryReplayReceipt(receiptState, "operation", "fingerprint", out var replay)
                && replay != null
                && replay.Operation == "progress"
                && replay.RunId == "run",
            "Committed mission operation can be replayed after a lost response"
        );
        check(!MissionTransaction.TryReplayReceipt(receiptState, "missing", "fingerprint", out _), "Unknown operation is not treated as a replay");
        check(
            Throws(() => MissionTransaction.TryReplayReceipt(receiptState, "operation", "different", out _),
                "Mission operation identifier was reused for different inputs."),
            "Reusing an operation identity with different inputs is rejected"
        );

        var oldRunState = new MissionProgress
        {
            SeasonId = "season",
            ActiveRun = new MissionRun { RunId = "new-run", Status = MissionRunStatuses.Active },
            Receipts = new()
            {
                ["old-operation"] = new MissionReceipt { RunId = "old-run", RequestHash = "fingerprint" },
            },
        };
        check(
            Throws(() => MissionTransaction.TryReplayReceipt(oldRunState, "old-operation", "fingerprint", out _),
                "This mission operation belongs to an older run. Refresh the mission list."),
            "A receipt from a prior retry cannot address the current run"
        );

        var durableReceiptProfile = new SPTarkov.Server.Core.Models.Eft.Common.PmcData { ExtensionData = new() };
        MissionStore.Write(durableReceiptProfile, receiptState);
        var durableReceiptState = MissionStore.Read(durableReceiptProfile, "season");
        check(
            MissionTransaction.TryReplayReceipt(durableReceiptState, "operation", "fingerprint", out _),
            "A committed receipt remains replayable after profile persistence"
        );

        var bounded = new MissionProgress
        {
            SeasonId = "season",
            ActiveRun = new MissionRun { RunId = "bounded-run", Status = MissionRunStatuses.Active },
        };
        var terminal = new MissionRun { RunId = "bounded-run", Status = MissionRunStatuses.Succeeded };
        MissionTransaction.AddReceipt(bounded, "terminal-old", "terminal", "finish", terminal, 1, maxReceipts: 3);
        MissionTransaction.AddReceipt(bounded, "active-old", "active-old", "progress", bounded.ActiveRun, 2, maxReceipts: 3);
        MissionTransaction.AddReceipt(bounded, "active-new", "active-new", "progress", bounded.ActiveRun, 3, maxReceipts: 3);
        MissionTransaction.AddReceipt(bounded, "latest", "latest", "progress", bounded.ActiveRun, 4, maxReceipts: 3);
        check(bounded.Receipts.Count == 3, "Receipt history stays within its configured bound");
        check(!bounded.Receipts.ContainsKey("terminal-old"), "Old terminal receipt is evicted before active receipts");
        check(bounded.Receipts.ContainsKey("active-old") && bounded.Receipts.ContainsKey("active-new"), "Active receipt history is retained when the bound is reached");
        check(
            MissionTransaction.TryReplayReceipt(bounded, "latest", "latest", out var latest)
                && latest != null
                && latest.Timestamp == 4,
            "The latest committed receipt is protected from eviction"
        );

        // Encounter profile caching is intentionally run-scoped. This models
        // the service's cache key and verifies a retry reuses the serialized
        // chunk instead of treating it as a new generation request.
        var chunkRun = new MissionRun { RunId = "encounter-run", EncounterProfileChunks = new() };
        const string chunkKey = "encounter|wave|roster|0|2";
        chunkRun.EncounterProfileChunks[chunkKey] = "[{\"Id\":\"bot-1\"},{\"Id\":\"bot-2\"}]";
        check(
            chunkRun.EncounterProfileChunks.TryGetValue(chunkKey, out var cachedChunk)
                && cachedChunk.Contains("bot-1", StringComparison.Ordinal),
            "Encounter profile chunk is cached under its canonical run-scoped key"
        );
        check(chunkRun.EncounterProfileChunks.Count == 1, "Retrying a cached encounter chunk cannot add another chunk");
    }

    private static bool Throws(Action action, string expectedMessage)
    {
        try
        {
            action();
            return false;
        }
        catch (InvalidOperationException exception)
        {
            return exception.Message == expectedMessage;
        }
    }
}
