using Newtonsoft.Json;

namespace SeasonalPerks.Shared;

/// <summary>The captured catalogue contract. Optional fields belong to different effect families.</summary>
public sealed class PerkEffect : ExtensibleJsonModel
{
    [JsonProperty("effectId")]
    public string EffectId { get; set; } = "";

    [JsonProperty("multiplicator", NullValueHandling = NullValueHandling.Ignore)]
    public double? Multiplier { get; set; }

    [JsonProperty("multiplicatorPrimary", NullValueHandling = NullValueHandling.Ignore)]
    public float? PrimaryMultiplier { get; set; }

    [JsonProperty("multiplicatorSecondary", NullValueHandling = NullValueHandling.Ignore)]
    public float? SecondaryMultiplier { get; set; }

    [JsonProperty("intValue", NullValueHandling = NullValueHandling.Ignore)]
    public int? IntValue { get; set; }

    [JsonProperty("bodyPartTypes", NullValueHandling = NullValueHandling.Ignore)]
    public List<string>? BodyPartTypes { get; set; }

    [JsonProperty("skillIds", NullValueHandling = NullValueHandling.Ignore)]
    public List<string>? SkillIds { get; set; }

    [JsonProperty("traderIds", NullValueHandling = NullValueHandling.Ignore)]
    public List<string>? TraderIds { get; set; }

    [JsonProperty("keyTypes", NullValueHandling = NullValueHandling.Ignore)]
    public List<string>? KeyTypes { get; set; }

    [JsonProperty("tradeAction", NullValueHandling = NullValueHandling.Ignore)]
    public string? TradeAction { get; set; }

    [JsonProperty("mode", NullValueHandling = NullValueHandling.Ignore)]
    public string? Mode { get; set; }

    [JsonProperty("itemFilter", NullValueHandling = NullValueHandling.Ignore)]
    public ItemFilter? ItemFilter { get; set; }

    [JsonProperty("randomSlotCount", NullValueHandling = NullValueHandling.Ignore)]
    public int? RandomSlotCount { get; set; }

    [JsonProperty("appliedRandomEffectCount", NullValueHandling = NullValueHandling.Ignore)]
    public int? AppliedRandomEffectCount { get; set; }

    [JsonProperty("subEffects", NullValueHandling = NullValueHandling.Ignore)]
    public Dictionary<string, AllergySubEffect>? SubEffects { get; set; }

    [JsonProperty("periodUnixSeconds", NullValueHandling = NullValueHandling.Ignore)]
    public long? PeriodUnixSeconds { get; set; }

    [JsonProperty("mailTemplateId", NullValueHandling = NullValueHandling.Ignore)]
    public string? MailTemplateId { get; set; }
}

public sealed class ItemFilter : ExtensibleJsonModel
{
    [JsonProperty("include", NullValueHandling = NullValueHandling.Ignore)]
    public List<ItemFilterRule>? Include { get; set; }

    [JsonProperty("exclude", NullValueHandling = NullValueHandling.Ignore)]
    public List<ItemFilterRule>? Exclude { get; set; }
}

public sealed class ItemFilterRule : ExtensibleJsonModel
{
    [JsonProperty("field")]
    public string Field { get; set; } = "";

    [JsonProperty("value")]
    public string Value { get; set; } = "";
}

public sealed class AllergySubEffect : ExtensibleJsonModel
{
    [JsonProperty("enabled", NullValueHandling = NullValueHandling.Ignore)]
    public bool? Enabled { get; set; }

    [JsonProperty("durationSeconds", NullValueHandling = NullValueHandling.Ignore)]
    public float? DurationSeconds { get; set; }

    [JsonProperty("amount", NullValueHandling = NullValueHandling.Ignore)]
    public float? Amount { get; set; }
}
