using Newtonsoft.Json;
using SPTarkov.Server.Core.Models.Eft.Common;
using WTT.Campaigns.Server.Profiles;
using WTT.Campaigns.Shared.Missions;

namespace WTT.Campaigns.Server.Missions;

internal static class MissionStore
{
    private const string Prefix = "wttCampaignsMissions:";

    internal static MissionProgress Read(PmcData pmc, string seasonId)
    {
        var key = Prefix + seasonId;
        if (!pmc.ExtensionData.TryGetValue(key, out _))
        {
            return new MissionProgress { SeasonId = seasonId };
        }

        var state = ProfileStateSerialization.Read<MissionProgress>(pmc, key)
            ?? throw new InvalidDataException("The saved mission state is invalid.");
        if (state.Version != 1 || state.SeasonId != seasonId)
        {
            throw new InvalidDataException("This mission save requires a compatible runtime.");
        }

        state.UnlockedMissionIds ??= new();
        state.CompletedMissionIds ??= new();
        state.Receipts ??= new();
        if (state.ActiveRun != null)
        {
            state.ActiveRun.CompletedCheckpointIds ??= new();
            state.ActiveRun.EncounterProfileChunks ??= new();
        }
        return state;
    }

    internal static void Write(PmcData pmc, MissionProgress state)
    {
        pmc.ExtensionData[Prefix + state.SeasonId] = JsonConvert.SerializeObject(state);
    }
}
