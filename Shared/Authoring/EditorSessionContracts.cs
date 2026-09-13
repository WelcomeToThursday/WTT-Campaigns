namespace WTT.Campaigns.Shared.Authoring;

public class EditorSessionRequest
{
    public int Version { get; set; } = 2;
    public string SessionId { get; set; } = "";
    public string DraftId { get; set; } = "";
    public string LayoutId { get; set; } = "";
    public string Location { get; set; } = "";
}

public sealed class EditorDraftChoice
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

public sealed class EditorLayoutChoice
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Location { get; set; } = "";
}

public sealed class EditorSessionResponse
{
    public int Version { get; set; } = 2;
    public string SessionId { get; set; } = "";
    public string ProfileId { get; set; } = "";
    public string ReturnProfileId { get; set; } = "";
    public string DraftId { get; set; } = "";
    public string LayoutId { get; set; } = "";
    public string Location { get; set; } = "";
    public List<EditorLayoutChoice> Layouts { get; set; } = new();
    public List<EditorDraftChoice> Drafts { get; set; } = new();
    public string? Error { get; set; }
}
