using System;

namespace SeasonalPerks.UI.Models;

[Serializable]
public sealed class HubState
{
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
}
