using Newtonsoft.Json;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Enums;
using WTT.Campaigns.Server.Profiles;

namespace WTT.Campaigns.Server.Progression;

internal sealed class ReputationRevision
{
    public string QuestId { get; set; } = "";
    public string RewardId { get; set; } = "";
    public string TraderId { get; set; } = "";
    public decimal Before { get; set; }
    public decimal After { get; set; }
}

internal sealed class ReputationReceipt
{
    public int Version { get; set; } = 1;
    public Dictionary<string, decimal> ObservedRewards { get; set; } = new();
}

internal static class ReputationMigration
{
    internal const string Key = "wttCampaignsReputationMigration";

    internal static bool Apply(PmcData profile, IReadOnlyList<ReputationRevision> revisions, Action beforeCredit)
    {
        lock (profile)
        {
            var receipt = ProfileStateSerialization.Read<ReputationReceipt>(profile, Key) ?? new();
            if (receipt.Version != 1)
            {
                throw new InvalidDataException("Unsupported campaign reputation migration receipt.");
            }
            var changed = false;
            var credits = new Dictionary<string, decimal>();
            foreach (var revision in revisions)
            {
                if (revision.Before < 0 || revision.After < revision.Before)
                {
                    throw new InvalidDataException("Invalid reputation migration adjustment.");
                }
                var key = revision.QuestId + "/" + revision.RewardId;
                var previous = receipt.ObservedRewards.GetValueOrDefault(key, revision.Before);
                if (receipt.ObservedRewards.ContainsKey(key) && previous >= revision.After)
                {
                    continue;
                }
                // Do not consume a pending credit when a trader record is absent.
                if (profile.TradersInfo?.ContainsKey(revision.TraderId) != true)
                {
                    continue;
                }
                if (profile.Quests?.Any(q => q.QId == revision.QuestId && q.Status == QuestStatusEnum.Success) == true)
                {
                    credits[revision.TraderId] = credits.GetValueOrDefault(revision.TraderId) + Math.Max(0, revision.After - previous);
                }
                // Also record uncompleted quests: their future hand-in earns the full new reward.
                receipt.ObservedRewards[key] = Math.Max(previous, revision.After);
                changed = true;
            }
            if (!changed)
            {
                return false;
            }
            if (credits.Values.Any(v => v > 0))
            {
                beforeCredit(); // Must succeed before any standings or receipt change.
            }
            foreach (var (trader, value) in credits)
            {
                var info = profile.TradersInfo![trader];
                info.Standing = (double)((decimal)(info.Standing ?? 0) + value);
            }
            // The receipt and standings are persisted together by SPT's native profile save.
            profile.ExtensionData[Key] = JsonConvert.SerializeObject(receipt);
            return true;
        }
    }
}
