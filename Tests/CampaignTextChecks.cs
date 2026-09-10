using WTT.Campaigns.Shared.Presentation;

namespace WTT.Campaigns.Tests;

internal static class CampaignTextChecks
{
    public static void Run(Action<bool, string> check)
    {
        foreach (
            var pair in new Dictionary<string, string>
            {
                ["PvE Season"] = "PvE Campaign",
                ["SEASONAL REWARDS"] = "CAMPAIGN REWARDS",
                ["Choose a season"] = "Choose a campaign",
                ["+ NEW SEASONAL CHARACTER"] = "+ NEW CAMPAIGN CHARACTER",
                ["Test Season"] = "Test Campaign",
                ["SEASONS"] = "CAMPAIGNS",
                ["Your seasonal character's season progress"] = "Your campaign character's campaign progress",
                ["<color=#83C5A9>SEASONS</color>\nSeason-wide rules"] = "<color=#83C5A9>CAMPAIGNS</color>\nCampaign-wide rules",
                ["Seasoned PMCs"] = "Seasoned PMCs",
                ["SeasonalPerks"] = "SeasonalPerks",
                ["SeasonId"] = "SeasonId",
            }
        )
        {
            check(CampaignText.Display(pair.Key) == pair.Value, "Campaign presentation: " + pair.Key);
        }
        const string unchanged = "A campaign with no legacy terminology";
        check(ReferenceEquals(CampaignText.Display(unchanged), unchanged), "Unchanged display copy reuses its string");
    }
}
