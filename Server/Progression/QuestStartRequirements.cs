using System.Security.Cryptography;
using System.Text;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Utils.Json;
using WTT.Campaigns.Shared.Progression;

namespace WTT.Campaigns.Server.Progression;

internal static class QuestStartRequirements
{
    internal static void Apply(Quest quest, TaskProgression spec)
    {
        // Captured lists can omit or reorder prerequisites even for Essential
        // Tasks. Never replace installed SPT conditions with a captured Start.
        quest.Conditions.AvailableForStart ??= [];
        Apply(quest.Conditions.AvailableForStart, quest.Id.ToString(), spec);
    }

    internal static void Apply(List<QuestCondition> native, string questId, TaskProgression spec)
    {
        // Older packages contain loyalty-only Start arrays. Always keep the
        // installed SPT requirements so an assembly-only hotfix repairs them too.
        if (
            spec.Tier <= 0
            || native.Any(c =>
                c.ConditionType == "TraderLoyalty" && c.Target?.Item == spec.TraderId && c.CompareMethod == ">=" && c.Value >= spec.Tier
            )
        )
        {
            return;
        }

        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("wtt-progression:" + questId)))[..24].ToLowerInvariant();
        native.Add(
            new QuestCondition
            {
                Id = new MongoId(id),
                ConditionType = "TraderLoyalty",
                Target = new ListOrT<string>(null, spec.TraderId),
                Value = spec.Tier,
                CompareMethod = ">=",
                DynamicLocale = false,
                VisibilityConditions = [],
                ParentId = "",
                GlobalQuestCounterId = "",
                Index = native.Count,
            }
        );
    }

    internal static bool CanStart(Quest quest, PmcData profile, long now)
    {
        return quest.Conditions.AvailableForStart?.All(c => Satisfied(c, profile, now)) == true;
    }

    private static IEnumerable<string> Targets(QuestCondition c)
    {
        return c.Target?.IsList == true ? c.Target.List ?? []
            : c.Target?.Item is string id ? [id]
            : [];
    }

    private static bool Satisfied(QuestCondition c, PmcData profile, long now)
    {
        if (c.ConditionType == "Level")
        {
            return TraderProgression.Compare(profile.Info?.Level ?? 1, c.Value ?? 0, c.CompareMethod);
        }
        if (c.ConditionType is "TraderLoyalty" or "TraderStanding")
        {
            return Targets(c)
                .Any(id =>
                    profile.TradersInfo?.TryGetValue(new MongoId(id), out var t) == true
                    && TraderProgression.Compare(
                        c.ConditionType == "TraderLoyalty" ? t.LoyaltyLevel ?? 1 : t.Standing ?? 0,
                        c.Value ?? 0,
                        c.CompareMethod
                    )
                );
        }
        if (c.ConditionType == "Quest")
        {
            return Targets(c)
                .Any(id =>
                    profile.Quests?.Any(q =>
                        q.QId == id
                        && c.Status?.Contains(q.Status) == true
                        && (
                            c.AvailableAfter.GetValueOrDefault() <= 0
                            || q.StatusTimers != null && q.StatusTimers.TryGetValue(q.Status, out var at) && at + c.AvailableAfter <= now
                        )
                    ) == true
                );
        }
        return false;
    }
}
