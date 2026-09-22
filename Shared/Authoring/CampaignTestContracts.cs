using WTT.Campaigns.Shared.Contracts;
using WTT.Campaigns.Shared.Missions;

namespace WTT.Campaigns.Shared.Authoring;

/// <summary>
/// Owner-authenticated controls for persistent draft-test characters. Native
/// gameplay uses the isolated test profile; controls use the launcher account.
/// </summary>
public static class CampaignTestRoutes
{
    public const string Prefix = "/wtt-campaigns/test-campaign";
    public const string Create = Prefix + "/create";
    public const string Status = Prefix + "/status";
    public const string Reset = Prefix + "/reset";
    public const string End = Prefix + "/end";
    public const string List = Prefix + "/list";
    public const string Apply = Prefix + "/apply";
    public const string Resume = Prefix + "/resume";
}

public static class CampaignTestActions
{
    public const string Create = "create";
    public const string Status = "status";
    public const string Reset = "reset";
    public const string End = "end";
    public const string List = "list";
    public const string Apply = "apply";
    public const string Resume = "resume";
}

/// <summary>
/// Revision-checked control request. Reset retires the prior profile identity;
/// apply retains the character and atomically replaces its tested snapshot.
/// </summary>
public class CampaignTestRequest
{
    public int Version { get; set; } = 2;
    public long ExpectedDraftRevision { get; set; }
    public long ExpectedLoadedRevision { get; set; }
    public string OperationId { get; set; } = "";
    public string EditorSessionId { get; set; } = "";
    public string DraftId { get; set; } = "";
    public string TestId { get; set; } = "";
    public string Action { get; set; } = CampaignTestActions.Create;
}

/// <summary>Projection returned by create/status/reset/end.</summary>
public sealed class CampaignTestResponse
{
    public int Version { get; set; } = 2;
    public long LoadedDraftRevision { get; set; }
    public long LatestDraftRevision { get; set; }
    public List<CampaignTestDraft> Drafts { get; set; } = new();
    public string? Error { get; set; }
    public string EditorSessionId { get; set; } = "";
    public string DraftId { get; set; } = "";
    public string TestId { get; set; } = "";
    public string ProfileId { get; set; } = "";
    public string SeasonId { get; set; } = "";
    public string ReturnProfileId { get; set; } = "";
    public string LayoutId { get; set; } = "";
    public string MissionId { get; set; } = "";
    public string QuestId { get; set; } = "";
    public string RunId { get; set; } = "";
    public long Revision { get; set; }
    public string Status { get; set; } = "";
    public string Message { get; set; } = "";
    public bool Disposable { get; set; }
    public bool SourcePreserved { get; set; } = true;
    public bool QuestAccepted { get; set; }
    public bool MissionCompleted { get; set; }
    public bool QuestCompleted { get; set; }
    public bool ReplayReady { get; set; }
    public bool Replayed { get; set; }
    public bool Committed { get; set; }
    public Snapshot? Snapshot { get; set; }
    public MissionResponse? Missions { get; set; }
}

public sealed class CampaignTestDraft
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public long Revision { get; set; }
    public bool HasProgress { get; set; }
}
