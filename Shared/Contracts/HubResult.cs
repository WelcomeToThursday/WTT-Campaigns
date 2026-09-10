namespace WTT.Campaigns.Shared.Contracts;

public sealed class HubResult
{
    public string Error { get; set; } = "";
    public string OperationId { get; set; } = "";
    public bool Committed { get; set; }
    public bool Ignored { get; set; }
    public string Message { get; set; } = "";
    public HubState? State { get; set; }
}
