using Newtonsoft.Json;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Utils;
using WTT.Campaigns.Server.Missions;
using WTT.Campaigns.Server.Seasons;
using WTT.Campaigns.Server.Story;
using WTT.Campaigns.Shared.Effects.Consumables;
using WTT.Campaigns.Shared.Missions;
using WTT.Campaigns.Shared.Profiles;
using WTT.Campaigns.Shared.Seasons;

namespace WTT.Campaigns.Server.Profiles;

public sealed class CampaignContentStamp
{
    public string Hash { get; set; } = "";
    public long Revision { get; set; }
    public Dictionary<string, string> Objectives { get; set; } = new();
    public HashSet<string> Quests { get; set; } = new();
    public Dictionary<string, string> Missions { get; set; } = new();
}

public static class CampaignReconciliation
{
    private const string StampKey = "wttCampaignsContent";
    private const string PerksKey = "wttCampaignsState";

    public static CampaignContentStamp Stamp(SeasonDefinition definition)
    {
        var result = new CampaignContentStamp { Hash = SeasonRepository.GameplayHash(definition), Revision = definition.Revision };
        foreach (var quest in definition.Quests)
        {
            result.Quests.Add(quest.Id);
            foreach (var condition in quest.AllConditions())
                result.Objectives[condition.Id] = SeasonRepository.GameplayHash(
                    new SeasonDefinition
                    {
                        Quests =
                        [
                            new()
                            {
                                Id = quest.Id,
                                Conditions = new() { AvailableForFinish = [condition] },
                            },
                        ],
                    }
                );
        }
        foreach (var mission in definition.Missions)
            result.Missions[mission.Id] = SeasonRepository.GameplayHash(
                new SeasonDefinition
                {
                    Missions = [mission],
                    MapLayouts = definition.MapLayouts.Where(l => l.Id == mission.LayoutId).ToList(),
                }
            );
        foreach (var link in definition.MissionLinks)
            result.Missions[link.MissionId] = SeasonRepository.GameplayHash(new SeasonDefinition { MissionLinks = [link] });
        return result;
    }

    public static bool Apply(PmcData pmc, SeasonDefinition definition, JsonUtil nativeJson, SeasonDefinition? baseline = null)
    {
        var next = Stamp(definition);
        var previous = ProfileStateSerialization.Read<CampaignContentStamp>(pmc, StampKey);
        if (previous?.Hash == next.Hash && previous.Revision == next.Revision)
            return false;
        previous ??= baseline == null ? new() : Stamp(baseline);
        var compatible = next.Objectives.Where(p => previous.Objectives.GetValueOrDefault(p.Key) == p.Value).Select(p => p.Key).ToHashSet();
        var owned = previous.Quests.Concat(next.Quests).ToHashSet();
        var retiredJson = ProfileStateSerialization.Read<List<string>>(pmc, "wttCampaignsRetiredQuests") ?? [];
        var retired = retiredJson
            .Select(value =>
                nativeJson.Deserialize<QuestStatus>(value) ?? throw new InvalidDataException("Invalid archived quest progress.")
            )
            .ToList();
        pmc.Quests ??= [];
        foreach (var quest in retired.Where(q => next.Quests.Contains(q.QId.ToString())).ToArray())
        {
            if (pmc.Quests.All(q => q.QId != quest.QId))
                pmc.Quests.Add(quest);
            retired.Remove(quest);
        }
        foreach (var quest in (pmc.Quests ?? []).Where(q => owned.Contains(q.QId.ToString())))
        {
            if (quest.Status.ToString() == "Success")
                continue;
            quest.CompletedConditions?.RemoveAll(id => !compatible.Contains(id.ToString()));
            // Counters may outlive CompletedConditions. Remove only changed owned
            // objective counters, never inventory or unrelated quest progression.
        }
        foreach (
            var quest in pmc
                .Quests.Where(q => previous.Quests.Contains(q.QId.ToString()) && !next.Quests.Contains(q.QId.ToString()))
                .ToArray()
        )
        {
            retired.RemoveAll(q => q.QId == quest.QId);
            retired.Add(quest);
            pmc.Quests.Remove(quest);
        }
        pmc.ExtensionData["wttCampaignsRetiredQuests"] = JsonConvert.SerializeObject(
            retired.Select(q => nativeJson.Serialize(q)!).ToList()
        );
        var changed = previous.Objectives.Keys.Concat(next.Objectives.Keys).Where(id => !compatible.Contains(id)).ToHashSet();
        foreach (var key in (pmc.TaskConditionCounters ?? []).Keys.Where(id => changed.Contains(id.ToString())).ToArray())
            pmc.TaskConditionCounters!.Remove(key);
        foreach (var quest in pmc.Quests ?? [])
        {
            if (
                quest.Status.ToString() == "AvailableForFinish"
                && definition
                    .Quests.Where(q => q.Id == quest.QId.ToString())
                    .SelectMany(q => q.AllConditions())
                    .Any(c => changed.Contains(c.Id))
            )
                quest.Status = SPTarkov.Server.Core.Models.Enums.QuestStatusEnum.Started;
        }
        var state = ProfileStateSerialization.Read<PerkState>(pmc, PerksKey) ?? new();
        state.SeasonalPerks = state
            .SeasonalPerks.Where(id => definition.Perks.Personal.Any(p => p.Id == id && p.Enabled))
            .Concat(definition.Rules.EnabledCommonIds)
            .Distinct()
            .ToList();
        ConsumableEffects.UpdateParameters(definition.Perks, state);
        state.GameplayHash = next.Hash;
        state.Revision++;
        pmc.ExtensionData[PerksKey] = JsonConvert.SerializeObject(state);
        var missions = MissionStore.Read(pmc, definition.Id);
        if (missions.ActiveRun is { } run && !MissionRunStatuses.IsTerminal(run.Status))
        {
            if (next.Missions.TryGetValue(run.MissionId, out var current) && previous.Missions.GetValueOrDefault(run.MissionId) == current)
            {
                // Unchanged linked missions already carry their pinned content hash.
                if (definition.MissionLinks.All(l => l.MissionId != run.MissionId))
                {
                    run.ContentRevision = definition.Revision;
                    run.ContentHash = next.Hash;
                }
            }
            else
            {
                run.Status = MissionRunStatuses.Cancelled;
                run.FailureReason = "Campaign content updated. Start the mission again.";
                run.CheckpointId = "";
                run.CompletedCheckpointIds.Clear();
                run.AttemptGeneration++;
            }
        }
        missions.UnlockedMissionIds.IntersectWith(
            definition.Missions.Select(m => m.Id).Concat(definition.MissionLinks.Select(l => l.MissionId))
        );
        missions.Revision++;
        MissionStore.Write(pmc, missions);
        var story = StoryStore.Read(pmc, definition.Id);
        story.Conversation = null;
        story.Raid = null;
        story.UnlockedMissionLinks.IntersectWith(definition.MissionLinks.Select(l => l.Id));
        story.Revision++;
        StoryStore.Write(pmc, story);
        pmc.ExtensionData[StampKey] = JsonConvert.SerializeObject(next);
        return true;
    }
}
