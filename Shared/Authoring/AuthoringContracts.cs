using WTT.Campaigns.Shared.Seasons;

namespace WTT.Campaigns.Shared.Authoring;

public sealed class CaptureTask
{
    public string Id { get; set; } = "";
    public string Tool { get; set; } = "Zone";
    public string TargetKind { get; set; } = "";
    public string TargetId { get; set; } = "";
    public string RecordId { get; set; } = "";
    public string Status { get; set; } = "Pending";
}

public class AuthoringRequest
{
    public int Version { get; set; } = 1;
    public string ClientId { get; set; } = "";
    public string RaidId { get; set; } = "";
    public string Location { get; set; } = "";
    public string Grant { get; set; } = "";
    public string DraftId { get; set; } = "";
    public string OperationId { get; set; } = "";
    public string TaskId { get; set; } = "";
    public string TaskStatus { get; set; } = "";
    public string ResultId { get; set; } = "";
    public List<string> NativeZoneIds { get; set; } = new();
    public List<string> Scenes { get; set; } = new();
    public bool Enabled { get; set; }
    public long Revision { get; set; }
    public SeasonDefinition? Baseline { get; set; }
    public SeasonDefinition? Definition { get; set; }
}

public sealed class AuthoringClient
{
    public string Id { get; set; } = "";
    public string CharacterId { get; set; } = "";
    public string RaidId { get; set; } = "";
    public string Location { get; set; } = "";
    public string DraftId { get; set; } = "";
    public DateTimeOffset LastSeen { get; set; }
    public List<CaptureTask> Tasks { get; set; } = new();
}

public sealed class DraftConflict
{
    public string Path { get; set; } = "";
    public string Local { get; set; } = "";
    public string Remote { get; set; } = "";
}

public sealed class AuthoringResponse
{
    public int Version { get; set; } = 1;
    public string? Error { get; set; }
    public string Grant { get; set; } = "";
    public string DraftId { get; set; } = "";
    public long Revision { get; set; }
    public SeasonDefinition? Definition { get; set; }
    public List<CaptureTask> Tasks { get; set; } = new();
    public List<DraftConflict> Conflicts { get; set; } = new();
    public SeasonDefinition? Candidate { get; set; }
    public SeasonDefinition? RemoteCandidate { get; set; }
}
