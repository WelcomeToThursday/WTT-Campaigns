namespace WTT.Campaigns.Shared.Authoring;

public class EditorEncounterProfilesRequest
{
    public int Version { get; set; } = 1;
    public string SessionId { get; set; } = "";
    public string LayoutId { get; set; } = "";
    public string Role { get; set; } = "assault";
    public string Difficulty { get; set; } = "normal";
    public int Count { get; set; } = 1;
}

public sealed class EditorEncounterProfilesResponse
{
    // Native JsonUtil owns this serialization, including SPT enum and MongoId converters.
    public string ProfilesJson { get; set; } = "[]";
    public string? Error { get; set; }
}
