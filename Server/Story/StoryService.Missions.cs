using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using WTT.Campaigns.Shared.Missions;
using WTT.Campaigns.Shared.Story;

namespace WTT.Campaigns.Server.Story;

public sealed partial class StoryService
{
    internal bool MissionLinkEligible(string id, SPTarkov.Server.Core.Models.Eft.Profile.SptProfile profile, string seasonId, CampaignMissionLink link)
    {
        var definition = repository.Runtime(seasonId).Definition.Story;
        var progress = StoryStore.Read(profile.CharacterData!.PmcData!, seasonId);
        var facts = definition == null ? new StoryFacts() : Facts(id, profile, progress, definition);
        return MissionLibrary.Eligible(link, definition, progress, facts);
    }

    internal void RefreshMissionLinksUnderLease(string id, SPTarkov.Server.Core.Models.Eft.Profile.SptProfile profile, string seasonId, StoryFacts? currentFacts = null)
    {
        var definition = repository.Runtime(seasonId).Definition;
        if (definition.MissionLinks.Count == 0) return;
        var pmc = profile.CharacterData!.PmcData!;
        var missions = WTT.Campaigns.Server.Missions.MissionStore.Read(pmc, seasonId);
        var changed = false;
        var progress = StoryStore.Read(pmc, seasonId);
        var facts = currentFacts ?? (definition.Story == null ? new StoryFacts() : Facts(id, profile, progress, definition.Story));
        foreach (var link in definition.MissionLinks)
        {
            if (MissionLibrary.Eligible(link, definition.Story, progress, facts)) changed |= missions.UnlockedMissionIds.Add(link.Id);
            if (missions.CompletedMissionIds.Contains(link.Id) && link.QuestId.Length > 0)
                ApplyMissionCompletionUnderLease(pmc, seasonId, new MissionDefinition { QuestId = link.QuestId, CompletionConditionId = link.CompletionConditionId });
        }
        if (changed)
        {
            missions.Revision++;
            WTT.Campaigns.Server.Missions.MissionStore.Write(pmc, missions);
        }
    }

    /// <summary>
    /// Applies a successful mission to the profile-scoped story variable and the
    /// linked native quest condition. The caller owns the character lease and saves
    /// the profile together with its mission receipt.
    /// </summary>
    internal void ApplyMissionCompletionUnderLease(PmcData pmc, string seasonId, MissionDefinition mission)
    {
        WTT.Campaigns.Server.Missions.MissionCompletion.Apply(pmc, repository.Runtime(seasonId).Definition, mission);
    }
}
