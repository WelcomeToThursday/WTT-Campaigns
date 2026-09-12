namespace WTT.Campaigns.Shared.Authoring;

public sealed class AuthoringSocketMessage
{
    public const string ChannelName = "wttCampaignsAuthoring";
    public string Channel { get; set; } = ChannelName;
    public string RequestId { get; set; } = "";

    // SPT's existing WebSocket listener caps complete messages at 4 MiB.
    public const int MaxBytes = 4 * 1024 * 1024;
    public string Operation { get; set; } = "";
    public AuthoringRequest Request { get; set; } = new();
}

public sealed class AuthoringSocketReply
{
    public string RequestId { get; set; } = "";
    public AuthoringResponse Response { get; set; } = new();
}
