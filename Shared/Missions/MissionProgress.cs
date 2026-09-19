namespace WTT.Campaigns.Shared.Missions;

public static class MissionRunStatuses
{
    public const string Prepared = "Prepared";
    public const string Active = "Active";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";

    public static bool IsTerminal(string? status) => status is Succeeded or Failed or Cancelled;
}

/// <summary>Durable campaign-character mission state. It is stored beside the PMC profile.</summary>
public sealed class MissionProgress
{
    public int Version { get; set; } = 1;
    public string SeasonId { get; set; } = "";
    public long Revision { get; set; }
    public HashSet<string> UnlockedMissionIds { get; set; } = new();
    public HashSet<string> CompletedMissionIds { get; set; } = new();
    public Dictionary<string, MissionReceipt> Receipts { get; set; } = new();
    public MissionRun? ActiveRun { get; set; }
}

/// <summary>Server-owned identity and progress for one prepared or active raid.</summary>
public sealed class MissionRun
{
    public int ContextVersion { get; set; }
    public string Scope { get; set; } = "";
    public string PackageId { get; set; } = "";
    public long PackageRevision { get; set; }
    public long AttemptGeneration { get; set; } = 1;
    public string CheckpointId { get; set; } = "";
    public bool Restoring { get; set; }
    public bool PlayerDefeated { get; set; }
    public bool TechnicalFailure { get; set; }
    public Dictionary<string, string> RestoredActorIds { get; set; } = new();
    public MissionLogicState Logic { get; set; } = new();
    public Dictionary<string, List<WTT.Campaigns.Shared.Native.NativeItem>> ContainerLoot { get; set; } = new();
    public string RunId { get; set; } = "";
    public string CharacterId { get; set; } = "";
    public string RaidId { get; set; } = "";
    public string MissionId { get; set; } = "";
    public string LayoutId { get; set; } = "";
    public long ContentRevision { get; set; }
    public string ContentHash { get; set; } = "";
    public string EncounterToken { get; set; } = "";
    public string Status { get; set; } = MissionRunStatuses.Prepared;
    public int NextCheckpointIndex { get; set; }
    public HashSet<string> CompletedCheckpointIds { get; set; } = new();
    public bool ExitReached { get; set; }
    public long PreparedAt { get; set; }
    public long StartedAt { get; set; }
    public long FinishedAt { get; set; }
    public string FailureReason { get; set; } = "";

    // Native raid finalization is separate from the mission attempt state. A
    // cancelled/failed run still has to pass through SPT's normal end-raid
    // reconciliation before a repeated end request can be suppressed.
    public bool NativeFinishCommitted { get; set; }

    // Generated native bot profiles are cached by roster chunk so retries cannot
    // mint additional profiles for the same server-issued mission run.
    public Dictionary<string, string> EncounterProfileChunks { get; set; } = new();
}

public sealed class MissionReceipt
{
    public long AttemptGeneration { get; set; } = 1;
    public string RequestHash { get; set; } = "";
    public long Revision { get; set; }
    public long Timestamp { get; set; }
    public string Operation { get; set; } = "";
    public string RunId { get; set; } = "";
    public string Status { get; set; } = "";
}
