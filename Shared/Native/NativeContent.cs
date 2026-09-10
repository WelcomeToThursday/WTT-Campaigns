using Newtonsoft.Json;
using WTT.Campaigns.Shared.Serialization;

namespace WTT.Campaigns.Shared.Native;

public static class NativeContent
{
    public static IEnumerable<NativeCondition> AllConditions(this NativeQuest quest)
    {
        return quest.Conditions.All().SelectMany(c => c.DescendantsAndSelf()).Where(c => !string.IsNullOrEmpty(c.ConditionType));
    }

    public static IEnumerable<NativeCondition> All(this NativeQuestConditions stages)
    {
        return stages.AvailableForStart.Concat(stages.AvailableForFinish).Concat(stages.Fail);
    }

    public static IEnumerable<NativeCondition> DescendantsAndSelf(this NativeCondition condition)
    {
        yield return condition;
        foreach (var child in (condition.Counter?.Conditions ?? new()).Concat(condition.VisibilityConditions))
        {
            foreach (var nested in child.DescendantsAndSelf())
            {
                yield return nested;
            }
        }
        if (condition.Props != null)
        {
            foreach (var nested in condition.Props.DescendantsAndSelf())
            {
                yield return nested;
            }
        }
    }

    public static List<NativeCondition> Stage(this NativeQuestConditions stages, string name)
    {
        return name switch
        {
            "AvailableForStart" => stages.AvailableForStart,
            "AvailableForFinish" => stages.AvailableForFinish,
            "Fail" => stages.Fail,
            _ => throw new ArgumentException("Unknown quest stage: " + name),
        };
    }

    public static IEnumerable<NativeReward> AllRewards(this NativeQuest quest)
    {
        return quest.Rewards.Values.SelectMany(r => r);
    }

    public static IEnumerable<NativeItem> AllItems(this NativeQuest quest)
    {
        return quest.AllRewards().SelectMany(r => r.Items);
    }

    public static string Text(this NativeQuest quest, string key, string language = "en")
    {
        return quest.Localization.GetValueOrDefault(language)?.GetValueOrDefault(key) ?? "";
    }

    public static Dictionary<string, string> English(this NativeQuest quest)
    {
        if (!quest.Localization.TryGetValue("en", out var texts))
        {
            quest.Localization["en"] = texts = new();
        }

        return texts;
    }

    public static void RemoveCondition(this NativeQuest quest, NativeCondition condition)
    {
        foreach (var stage in new[] { quest.Conditions.AvailableForStart, quest.Conditions.AvailableForFinish, quest.Conditions.Fail })
        {
            if (stage.Remove(condition))
            {
                return;
            }
        }

        foreach (var parent in quest.AllConditions())
        {
            if (parent.Counter?.Conditions.Remove(condition) == true || parent.VisibilityConditions.Remove(condition))
            {
                return;
            }
        }
    }
}

public sealed class HubGameplayDefinition
{
    public string Id { get; set; } = "";
    public string SeasonId { get; set; } = "";
    public int ExchangeRate { get; set; }
    public HubItemExchange ItemExchange { get; set; } = new();
    public List<HubGameplayDocument> Documents { get; set; } = new();
    public Dictionary<string, HubGameplayReward> Rewards { get; set; } = new();
    public List<NativeOffer> Offers { get; set; } = new();
}

public sealed class HubItemExchange
{
    [JsonProperty("itemId")]
    public string ItemId { get; set; } = "";

    [JsonProperty("requiredDocuments")]
    public int RequiredDocuments { get; set; }
}

public sealed class HubGameplayDocument
{
    [JsonProperty("id")]
    public string Id { get; set; } = "";

    [JsonProperty("itemId")]
    public string ItemId { get; set; } = "";
}

public sealed class HubGameplayReward
{
    public List<NativeReward> Grants { get; set; } = new();
    public List<NativeCondition> Conditions { get; set; } = new();
}
