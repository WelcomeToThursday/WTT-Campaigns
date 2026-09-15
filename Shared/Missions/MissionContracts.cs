using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Shared.Missions;

/// <summary>Payload used by the missions list, prepare, descriptor, progress and cancel routes.</summary>
public class MissionRequest
{
    public int Version { get; set; } = 1;
    public string SeasonId { get; set; } = "";
    public string CharacterId { get; set; } = "";
    public string OperationId { get; set; } = "";
    public long ExpectedRevision { get; set; }
    public string MissionId { get; set; } = "";
    public string RunId { get; set; } = "";
    public string RaidId { get; set; } = "";
    public string CheckpointId { get; set; } = "";
    public string Kind { get; set; } = "";
}

public sealed class MissionSummary
{
    public MissionDefinition Definition { get; set; } = new();
    public string Status { get; set; } = "Locked";
    public bool Unlocked { get; set; }
    public bool Completed { get; set; }
    public bool Active { get; set; }
    public string FailureReason { get; set; } = "";
}

/// <summary>Immutable content and server-issued identity required to enter a mission raid.</summary>
public sealed class MissionDescriptor
{
    public MissionDefinition Definition { get; set; } = new();
    public MapLayout Layout { get; set; } = new();
    public List<SeasonZone> Zones { get; set; } = new();
    public string CharacterId { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string RunId { get; set; } = "";
    public string RaidId { get; set; } = "";
    public long ContentRevision { get; set; }
    public string ContentHash { get; set; } = "";
    public string EncounterToken { get; set; } = "";
    public bool MissionOnlyExtracts { get; set; } = true;
}

public sealed class MissionResponse
{
    public int Version { get; set; } = 1;
    public string? Error { get; set; }
    public string SeasonId { get; set; } = "";
    public string CharacterId { get; set; } = "";
    public long Revision { get; set; }
    public List<MissionSummary> Missions { get; set; } = new();
    public MissionDescriptor? Descriptor { get; set; }
    public MissionRun? Run { get; set; }
    public bool Replayed { get; set; }
    public bool Committed { get; set; }
    public string Message { get; set; } = "";
}

/// <summary>Requests one bounded native profile chunk for an authored mission roster.</summary>
public class MissionEncounterProfilesRequest : MissionRequest
{
    public string EncounterToken { get; set; } = "";
    public string EncounterId { get; set; } = "";
    public string WaveId { get; set; } = "";
    public string RosterId { get; set; } = "";
    public int Offset { get; set; }
    public int Count { get; set; }
}
