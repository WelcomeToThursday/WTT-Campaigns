using Newtonsoft.Json;
using SeasonalPerks.Shared.Story;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;

namespace SeasonalPerks.Server.Story;

internal static class StoryStore
{
    internal static StoryProgress Read(PmcData pmc, string seasonId)
    {
        if (!pmc.ExtensionData.TryGetValue("wttSeasonalStory:" + seasonId, out var raw))
        {
            return new StoryProgress { SeasonId = seasonId };
        }
        var text = raw is System.Text.Json.JsonElement element ? element.GetString() : raw.ToString();
        var state =
            JsonConvert.DeserializeObject<StoryProgress>(text!) ?? throw new InvalidOperationException("The saved story state is invalid.");
        if (state.Version != 1 || state.SeasonId != seasonId)
        {
            throw new InvalidOperationException("This story save requires a compatible runtime.");
        }
        return state;
    }

    internal static void Write(PmcData pmc, StoryProgress state)
    {
        pmc.ExtensionData["wttSeasonalStory:" + state.SeasonId] = JsonConvert.SerializeObject(state);
        pmc.Variables ??= new();
        foreach (var variable in state.Variables)
        {
            pmc.Variables[new MongoId(variable.Key)] = variable.Value;
        }
        foreach (var id in state.CompletedBindings.Concat(state.CompletedItems))
        {
            pmc.Variables[new MongoId(id)] = 1;
        }
    }
}
