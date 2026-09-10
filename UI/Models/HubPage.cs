using System;

namespace WTT.Campaigns.UI.Models;

[Serializable]
public sealed class HubPage
{
    public int PreviousRequirement = 0;
    public HubReward[] Rewards = Array.Empty<HubReward>();

    public int ClaimedCount = 0;
}
