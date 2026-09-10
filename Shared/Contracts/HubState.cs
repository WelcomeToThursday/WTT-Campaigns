using System;

namespace WTT.Campaigns.Shared.Contracts;

public sealed class HubState
{
    public string SeasonName { get; set; } = "Season One";
    public long PackRevision { get; set; }
    public string BadgeImage { get; set; } = "";
    public string BannerImage { get; set; } = "";
    public bool LegacyBranding { get; set; } = true;
    public int WindowSeconds { get; set; } = 23 * 60 * 60;
    public string Id { get; set; } = "";
    public string SeasonId { get; set; } = "";
    public HubPage[] Pages { get; set; } = Array.Empty<HubPage>();
    public HubReward[] SeasonalRewards { get; set; } = Array.Empty<HubReward>();
    public HubDocument[] Documents { get; set; } = Array.Empty<HubDocument>();
    public HubSlide[] Slides { get; set; } = Array.Empty<HubSlide>();
    public string UniversalImage { get; set; } = "";
    public string UniversalUnavailableImage { get; set; } = "";
    public int UniversalCount { get; set; }
    public int DocumentLimit { get; set; } = 30;
    public int ClaimedRewards { get; set; } = 0;
    public bool PreviewOnly { get; set; } = true;

    public long Revision { get; set; } = 0;
    public int RemainingDocuments { get; set; } = 0;
    public long NextResetTime { get; set; } = 0;
    public long Tarcoins { get; set; } = 0;
    public string Error { get; set; } = "";
    public string ExchangeUnavailableReason { get; set; } = "";
    public string CrateUnavailableReason { get; set; } = "";
    public int ExchangeRate { get; set; } = 0;
    public int CrateCost { get; set; } = 0;
}
