namespace WTT.Campaigns.Shared.Hub;

public sealed class HubReceipt
{
    public string Fingerprint { get; set; } = "";
    public long Revision { get; set; }
    public string Message { get; set; } = "";
}
