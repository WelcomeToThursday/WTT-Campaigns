using WTT.Campaigns.Shared.Missions;

namespace WTT.Campaigns.Shared.Authoring;

/// <summary>
/// HTTP boundary for the disposable, draft-backed mission rehearsal exposed by
/// Campaign Editor. The full campaign rehearsal has its own native campaign
/// contract and is deliberately kept out of this quick-test boundary.
/// </summary>
public static class EditorTestRoutes
{
    public const string Mission = "/wtt-campaigns/editor/test-mission";
}

public static class EditorTestActions
{
    public const string Prepare = "prepare";
    public const string Progress = "progress";
    public const string Reset = "reset";
    public const string End = "end";
}

/// <summary>Request for a quick, draft-backed editor mission rehearsal.</summary>
public class EditorTestMissionRequest
{
    public int Version { get; set; } = 1;
    public string SessionId { get; set; } = "";
    public string DraftId { get; set; } = "";
    public string LayoutId { get; set; } = "";
    public string MissionId { get; set; } = "";
    public string RunId { get; set; } = "";
    public string CheckpointId { get; set; } = "";
    public string Kind { get; set; } = "";

    /// <summary>
    /// Stable identity for one route transition. The client reuses it if the
    /// response is lost, allowing the server to treat the retry as the same
    /// idempotent operation.
    /// </summary>
    public string OperationId { get; set; } = "";
    public string Action { get; set; } = EditorTestActions.Prepare;
    public bool UseEncounters { get; set; } = true;
}

/// <summary>Disposable response for a quick draft rehearsal.</summary>
public sealed class EditorTestMissionResponse
{
    public int Version { get; set; } = 1;
    public string? Error { get; set; }
    public string SessionId { get; set; } = "";
    public string ProfileId { get; set; } = "";
    public string DraftId { get; set; } = "";
    public string LayoutId { get; set; } = "";
    public string MissionId { get; set; } = "";
    public string RunId { get; set; } = "";
    public long ContentRevision { get; set; }
    public string ContentHash { get; set; } = "";
    public string Status { get; set; } = "";
    public bool Disposable { get; set; } = true;
    public bool SourcePreserved { get; set; } = true;
    public bool Replayed { get; set; }
    public bool Committed { get; set; }
    public bool UseEncounters { get; set; } = true;
    public MissionDescriptor? Descriptor { get; set; }
    public MissionRun? Run { get; set; }
    public string Message { get; set; } = "";
}
