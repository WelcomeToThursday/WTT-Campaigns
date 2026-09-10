using System;

namespace WTT.Campaigns.Shared.Contracts;

public sealed class HubDocument
{
    public string ItemId { get; set; } = "";
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Image { get; set; } = "";
    public string UnavailableImage { get; set; } = "";
    public int Count { get; set; } = 0;
}
