using Newtonsoft.Json;
using SPT.Common.Http;
using WTT.Campaigns.Shared.Progression;

namespace WTT.Campaigns.Client.Progression;

internal static class ProgressionClient
{
    internal static ProgressionMetadata? Metadata { get; private set; }

    internal static void Reset() => Metadata = null;

    internal static void Load()
    {
        if (Metadata != null)
            return;
        var data = JsonConvert.DeserializeObject<ProgressionMetadata>(RequestHandler.PostJson("/wtt-campaigns/progression", "{}"));
        if (data?.Version != 1)
            throw new System.InvalidOperationException("Update the trader progression client and server together.");
        Metadata = data;
    }

    internal static int Tier(string id) => Metadata?.Quests.TryGetValue(id, out var task) == true ? task.Tier : 0;

    internal static bool Applied(string id) => Metadata?.Traders.Contains(id) == true;
}
