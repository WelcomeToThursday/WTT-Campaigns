using Newtonsoft.Json;
using WTT.Campaigns.Shared.Contracts;

namespace WTT.Campaigns.Tests;

internal static class HubChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "data/hub.json");
        var state = JsonConvert.DeserializeObject<HubState>(File.ReadAllText(path))!;
        check(state.Pages.Length == 12 && state.Pages.Sum(p => p.Rewards.Length) == 53, "Captured Battle Pass page and reward counts");
        check(
            state.SeasonalRewards.Length == 5 && state.Documents.Length == 8 && state.Slides.Length == 5,
            "Captured campaign content counts"
        );
        check(
            state.PreviewOnly && state.ClaimedRewards == 0 && state.UniversalCount == 0 && state.Documents.All(d => d.Count == 0),
            "Installed hub has neutral progress"
        );
        var rewards = state.Pages.SelectMany(p => p.Rewards).Concat(state.SeasonalRewards).ToArray();
        check(rewards.Select(r => r.Id).Distinct().Count() == rewards.Length, "Stable unique reward IDs");
        check(rewards.All(r => !r.Claimed && !string.IsNullOrWhiteSpace(r.Name)), "Every reward has a name and is unclaimed");
        var documents = state.Documents.Select(d => d.Id).ToHashSet();
        check(
            rewards.SelectMany(r => r.Costs).All(c => c.Count > 0 && documents.Contains(c.DocumentId)),
            "Every requirement resolves to a document"
        );
        foreach (var page in state.Pages)
        {
            var cells = new HashSet<(int, int)>();
            foreach (var reward in page.Rewards)
            {
                for (var x = reward.X; x < reward.X + reward.Width; x++)
                {
                    for (var y = reward.Y; y < reward.Y + reward.Height; y++)
                    {
                        check(x is >= 0 and < 2 && y is >= 0 and < 3 && cells.Add((x, y)), "Captured reward spans fit without overlap");
                    }
                }
            }
        }
        check(
            state.Pages[0].Rewards[1].Name == "Tarcoins (50)" && state.Pages[9].Rewards[3].Name == "Tarcoins (100)",
            "Currency quantities retained"
        );
        check(state.UniversalImage != state.UniversalUnavailableImage, "Universal document has separate available and unavailable artwork");
        var roundTrip = JsonConvert.DeserializeObject<HubState>(JsonConvert.SerializeObject(state))!;
        check(
            JsonConvert.SerializeObject(roundTrip) == JsonConvert.SerializeObject(state),
            "Hub contract round trip preserves all presentation content"
        );
    }
}
