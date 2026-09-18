using Newtonsoft.Json;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using WTT.Campaigns.Server.Profiles;
using WTT.Campaigns.Shared.Story;

namespace WTT.Campaigns.Server.Story;

internal static class StoryStore
{
    internal static StoryProgress Read(PmcData pmc, string seasonId)
    {
        if (!pmc.ExtensionData.TryGetValue("wttCampaignsStory:" + seasonId, out var raw))
        {
            return new StoryProgress { SeasonId = seasonId };
        }
        var state =
            ProfileStateSerialization.Read<StoryProgress>(pmc, "wttCampaignsStory:" + seasonId)
            ?? throw new InvalidOperationException("The saved story state is invalid.");
        if (state.Version != 1 || state.SeasonId != seasonId)
        {
            throw new InvalidOperationException("This story save requires a compatible runtime.");
        }
        state.UnlockedMissionLinks ??= new();
        return state;
    }

    internal static void Write(PmcData pmc, StoryProgress state)
    {
        pmc.ExtensionData["wttCampaignsStory:" + state.SeasonId] = JsonConvert.SerializeObject(state);
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
