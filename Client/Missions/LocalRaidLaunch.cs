using EFT;
using EFT.Bots;
using JsonType;

namespace WTT.Campaigns.Client.Missions;

internal static class LocalRaidLaunch
{
    internal static RaidSettings CreateSettings(LocationSettings locations, LocationSettings.Location location) =>
        // The menu matchmaker retains its settings and can restore them asynchronously.
        // A direct launch must own a fresh instance, including on repeated map entry.
        new(
            ESideType.Pmc,
            EDateTime.CURR,
            ERaidMode.Local,
            new TimeAndWeatherSettings(false, false, 0, 0, 0, 0, (int)ETimeFlowType.x0, 12),
            locations
        )
        {
            SelectedLocation = location,
            BotSettings = new BotControllerSettings(false, EBotAmount.NoBots),
            IsPveOffline = false,
        };
}
