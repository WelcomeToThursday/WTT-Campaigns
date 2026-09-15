using WTT.Campaigns.Shared.Contracts;
using WTT.Campaigns.Shared.Missions;

namespace WTT.Campaigns.Shared.Authoring;

/// <summary>
/// HTTP contract for the full, disposable campaign rehearsal.  This is kept
/// separate from the quick editor mission contract: the full path creates a
/// normal native PMC and is addressed by its temporary profile identity while
/// its control endpoints remain authenticated by the editor owner.
/// </summary>
public static class CampaignTestRoutes
{
    public const string Prefix = "/wtt-campaigns/test-campaign";
    public const string Create = Prefix + "/create";
    public const string Status = Prefix + "/status";
    public const string Reset = Prefix + "/reset";
    public const string End = Prefix + "/end";
}

public static class CampaignTestActions
{
    public const string Create = "create";
    public const string Status = "status";
    public const string Reset = "reset";
    public const string End = "end";
}

/// <summary>
/// Owner-authenticated control request for a disposable campaign profile.
/// TestId is empty for create and is always echoed by the server after a
/// reset, since reset retires the previous profile and starts from a clean
/// native profile.
/// </summary>
public class CampaignTestRequest
{
    public int Version { get; set; } = 1;
    public string EditorSessionId { get; set; } = "";
    public string DraftId { get; set; } = "";
    public string TestId { get; set; } = "";
    public string Action { get; set; } = CampaignTestActions.Create;
}

/// <summary>Projection returned by create/status/reset/end.</summary>
public sealed class CampaignTestResponse
{
    public int Version { get; set; } = 1;
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
    public bool Disposable { get; set; } = true;
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
