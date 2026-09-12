using Newtonsoft.Json;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Commerce;
using SPTarkov.Server.Core.Helpers.Quest;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Cloners;
using WTT.Campaigns.Server.Hub;
using WTT.Campaigns.Server.Profiles;
using WTT.Campaigns.Shared.Progression;
using Path = System.IO.Path;

namespace WTT.Campaigns.Server.Progression;

[Injectable(InjectionType.Singleton, OnLoadOrder.PostLoad + 700)]
public sealed class ProgressionService(TemplateTable templates, TradersTable traders, JsonUtil json, ICloner cloner, RewardHelper rewards)
    : IOnLoad
{
    public TraderProgression Data { get; private set; } = new();
    public ProgressionMetadata Metadata { get; } = new();
    private readonly HashSet<string> _hidden = new();
    public HashSet<string> StandingConditions { get; } = new();

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        Data = JsonConvert.DeserializeObject<TraderProgression>(
            File.ReadAllText(Path.Combine(Server.Metadata.DirectoryPath, "data", "trader-progression.json"))
        )!;
        if (Data.Version != 1)
        {
            throw new InvalidDataException("Unsupported trader progression data version.");
        }
        foreach (var (id, levels) in Data.Traders)
        {
            if (
                !traders.TryGetValue(new MongoId(id), out var trader)
                || trader.Base?.LoyaltyLevels is not { } thresholds
                || thresholds.Count != levels.Count
            )
            {
                continue;
            }
            for (var i = 0; i < levels.Count; i++)
            {
                thresholds[i].MinLevel = levels[i].Level;
                thresholds[i].MinStanding = levels[i].Standing;
                thresholds[i].MinSalesSum = 0;
            }
            Metadata.Traders.Add(id);
        }
        foreach (var (id, spec) in Data.Quests)
        {
            if (!templates.Quests.TryGetValue(new MongoId(id), out var quest) || !Metadata.Traders.Contains(spec.TraderId))
            {
                continue;
            }
            if (
                quest.ExtensionData?.TryGetValue("notDisplayedQuest", out var hidden) == true
                && string.Equals(hidden?.ToString(), "true", StringComparison.OrdinalIgnoreCase)
            )
            {
                _hidden.Add(id);
            }
            quest.TraderId = new MongoId(spec.TraderId);
            QuestStartRequirements.Apply(quest, spec);
            foreach (var condition in quest.Conditions.AvailableForStart ?? [])
            {
                if (condition.ConditionType == "TraderStanding")
                {
                    StandingConditions.Add(condition.Id.ToString());
                }
            }
            quest.Rewards ??= [];
            foreach (var stage in quest.Rewards.Keys.Union(spec.Reputation.Keys).ToArray())
            {
                var stageRewards = quest.Rewards.GetValueOrDefault(stage) ?? [];
                stageRewards.RemoveAll(r => r.Type == RewardType.TraderStanding);
                if (spec.Reputation.TryGetValue(stage, out var captured))
                {
                    stageRewards.AddRange(json.Deserialize<List<Reward>>(Newtonsoft.Json.JsonConvert.SerializeObject(captured))!);
                }
                quest.Rewards[stage] = stageRewards;
            }
            Metadata.Quests[id] = new TaskTier { TraderId = spec.TraderId, Tier = spec.Tier };
        }
        return Task.CompletedTask;
    }

    public void Recalculate(PmcData profile)
    {
        if (profile.TradersInfo == null || profile.Info == null)
        {
            return;
        }
        foreach (var id in Metadata.Traders)
        {
            if (profile.TradersInfo.TryGetValue(new MongoId(id), out var info))
            {
                info.LoyaltyLevel = TraderProgression.Loyalty(Data.Traders[id], profile.Info.Level ?? 1, info.Standing ?? 0);
            }
        }
    }

    public bool Eligible(Quest quest, PmcData profile, QuestHelper helper)
    {
        return profile.Info != null
            && !_hidden.Contains(quest.Id.ToString())
            && !helper.QuestIsForOtherSide(profile.Info.Side, quest.Id)
            && !helper.QuestIsProfileBlacklisted(profile.Info.GameVersion, quest.Id)
            && helper.QuestIsProfileWhitelisted(profile.Info.GameVersion, quest.Id)
            && helper.ShowEventQuestToPlayer(quest.Id)
            && profile.TradersInfo?.TryGetValue(quest.TraderId, out var trader) == true
            && trader.Unlocked == true
            && trader.Disabled != true;
    }

    public bool CanStart(Quest quest, PmcData profile, long now)
    {
        return QuestStartRequirements.CanStart(quest, profile, now);
    }

    public List<Quest> WithPreviews(
        List<Quest> result,
        PmcData profile,
        QuestHelper helper,
        string season,
        HubQuestService hub,
        TimeUtil time
    )
    {
        Recalculate(profile);
        var byId = result.ToDictionary(q => q.Id.ToString());
        foreach (var (id, spec) in Metadata.Quests)
        {
            var quest = templates.Quests[new MongoId(id)];
            var state = profile.Quests?.FirstOrDefault(q => q.QId == id);
            // Existing accepted/finished quests retain their native progress even after a loyalty loss.
            if (
                state != null
                && state.Status is not (QuestStatusEnum.Locked or QuestStatusEnum.AvailableForStart or QuestStatusEnum.AvailableAfter)
            )
            {
                continue;
            }
            if (state?.Status == QuestStatusEnum.AvailableAfter && state.AvailableAfter > time.GetTimeStamp())
            {
                continue;
            }
            if (!Eligible(quest, profile, helper) || !hub.Allowed(id, season))
            {
                byId.Remove(id);
                continue;
            }
            var available = CanStart(quest, profile, time.GetTimeStamp());
            if (!available && (spec.Tier == 0 || quest.SecretQuest == true))
            {
                byId.Remove(id);
                continue;
            }
            var copy = byId.TryGetValue(id, out var existing) ? existing : cloner.Clone(quest)!;
            if (copy.Rewards != null)
            {
                foreach (var stage in copy.Rewards.Keys.ToArray())
                {
                    copy.Rewards[stage] = copy.Rewards[stage]
                        .Where(r => rewards.RewardIsForGameEdition(r, profile.Info!.GameVersion ?? "standard"))
                        .ToList();
                }
            }
            copy.SptStatus = available ? QuestStatusEnum.AvailableForStart : QuestStatusEnum.Locked;
            byId[id] = copy;
        }
        return byId.Values.Where(q => hub.Allowed(q.Id.ToString(), season)).ToList();
    }
}
