using System;

namespace SeasonalPerks.UI.Models;

[Serializable]
public sealed class HubState
{
    public string SeasonName = "Season One";
    public long PackRevision;
    public string BadgeImage = "";
    public string BannerImage = "";
    public bool LegacyBranding = true;
    public int WindowSeconds = 23 * 60 * 60;
    public string Id = "";
    public string SeasonId = "";
    public HubPage[] Pages = Array.Empty<HubPage>();
    public HubReward[] SeasonalRewards = Array.Empty<HubReward>();
    public HubDocument[] Documents = Array.Empty<HubDocument>();
    public HubSlide[] Slides = Array.Empty<HubSlide>();
    public string UniversalImage = "";
    public string UniversalUnavailableImage = "";
    public int UniversalCount;
    public int DocumentLimit = 30;
    public int ClaimedRewards = 0;
    public bool PreviewOnly = true;

    public long Revision = 0;
    public int RemainingDocuments = 0;
    public long NextResetTime = 0;
    public long Tarcoins = 0;
    public string Error = "";
    public string ExchangeUnavailableReason = "";
    public string CrateUnavailableReason = "";
    public int ExchangeRate = 0;
    public int CrateCost = 0;
}
