using Newtonsoft.Json;
using WTT.Campaigns.Shared.Serialization;

namespace WTT.Campaigns.Shared.Native;

public sealed class NativeCondition : NativeModel
{
    [JsonProperty("hint", NullValueHandling = NullValueHandling.Ignore)]
    public string? Hint { get; set; }

    [JsonProperty("conditionId", NullValueHandling = NullValueHandling.Ignore)]
    public string? ConditionId { get; set; }

    [JsonProperty("index", NullValueHandling = NullValueHandling.Ignore)]
    public int? Index { get; set; }

    [JsonProperty("dynamicLocale", NullValueHandling = NullValueHandling.Ignore)]
    public bool? DynamicLocale { get; set; }

    [JsonProperty("visibilityConditions", NullValueHandling = NullValueHandling.Ignore)]
    public List<NativeCondition> VisibilityConditions { get; set; } = new();

    [JsonProperty("globalQuestCounterId", NullValueHandling = NullValueHandling.Ignore)]
    public string? GlobalQuestCounterId { get; set; }

    [JsonProperty("parentId", NullValueHandling = NullValueHandling.Ignore)]
    public string? ParentId { get; set; }

    [JsonProperty("dialogId", NullValueHandling = NullValueHandling.Ignore)]
    public string? DialogId { get; set; }

    [JsonProperty("entryPoint", NullValueHandling = NullValueHandling.Ignore)]
    public string? EntryPoint { get; set; }

    [JsonProperty("fromTraderId", NullValueHandling = NullValueHandling.Ignore)]
    public string? FromTraderId { get; set; }

    [JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
    public string Id { get; set; } = "";

    [JsonProperty("props", NullValueHandling = NullValueHandling.Ignore)]
    public NativeCondition? Props { get; set; }

    [JsonProperty("questNoteId", NullValueHandling = NullValueHandling.Ignore)]
    public string? QuestNoteId { get; set; }

    [JsonProperty("target", NullValueHandling = NullValueHandling.Ignore)]
    public StringTargets? Target { get; set; }

    [JsonProperty("status", NullValueHandling = NullValueHandling.Ignore)]
    public List<string>? Status { get; set; }

    [JsonProperty("availableAfter", NullValueHandling = NullValueHandling.Ignore)]
    public int? AvailableAfter { get; set; }

    [JsonProperty("dispersion", NullValueHandling = NullValueHandling.Ignore)]
    public double? Dispersion { get; set; }

    [JsonProperty("isFinisher", NullValueHandling = NullValueHandling.Ignore)]
    public bool? IsFinisher { get; set; }

    [JsonProperty("conditionType", NullValueHandling = NullValueHandling.Ignore)]
    public string ConditionType { get; set; } = "";

    [JsonProperty("value", NullValueHandling = NullValueHandling.Ignore)]
    public double? Value { get; set; }

    [JsonProperty("compareMethod", NullValueHandling = NullValueHandling.Ignore)]
    public string? CompareMethod { get; set; }

    [JsonProperty("showCounter", NullValueHandling = NullValueHandling.Ignore)]
    public bool? ShowCounter { get; set; }

    [JsonProperty("minDurability", NullValueHandling = NullValueHandling.Ignore)]
    public double? MinDurability { get; set; }

    [JsonProperty("maxDurability", NullValueHandling = NullValueHandling.Ignore)]
    public double? MaxDurability { get; set; }

    [JsonProperty("dogtagLevel", NullValueHandling = NullValueHandling.Ignore)]
    public int? DogtagLevel { get; set; }

    [JsonProperty("onlyFoundInRaid", NullValueHandling = NullValueHandling.Ignore)]
    public bool? OnlyFoundInRaid { get; set; }

    [JsonProperty("isEncoded", NullValueHandling = NullValueHandling.Ignore)]
    public bool? IsEncoded { get; set; }

    [JsonProperty("countInRaid", NullValueHandling = NullValueHandling.Ignore)]
    public bool? CountInRaid { get; set; }

    [JsonProperty("includeEquipment", NullValueHandling = NullValueHandling.Ignore)]
    public bool? IncludeEquipment { get; set; }

    [JsonProperty("type", NullValueHandling = NullValueHandling.Ignore)]
    public string? Type { get; set; }

    [JsonProperty("oneSessionOnly", NullValueHandling = NullValueHandling.Ignore)]
    public bool? OneSessionOnly { get; set; }

    [JsonProperty("completeInSeconds", NullValueHandling = NullValueHandling.Ignore)]
    public double? CompleteInSeconds { get; set; }

    [JsonProperty("doNotResetIfCounterCompleted", NullValueHandling = NullValueHandling.Ignore)]
    public bool? DoNotResetIfCounterCompleted { get; set; }

    [JsonProperty("isResetOnConditionFailed", NullValueHandling = NullValueHandling.Ignore)]
    public bool? IsResetOnConditionFailed { get; set; }

    [JsonProperty("isNecessary", NullValueHandling = NullValueHandling.Ignore)]
    public bool? IsNecessary { get; set; }

    [JsonProperty("isNotGroupProgress", NullValueHandling = NullValueHandling.Ignore)]
    public bool? IsNotGroupProgress { get; set; }

    [JsonProperty("counter", NullValueHandling = NullValueHandling.Ignore)]
    public NativeCounter? Counter { get; set; }

    [JsonProperty("epicGamesId", NullValueHandling = NullValueHandling.Ignore)]
    public string? EpicGamesId { get; set; }

    [JsonProperty("steamGamesId", NullValueHandling = NullValueHandling.Ignore)]
    public string? SteamGamesId { get; set; }

    [JsonProperty("plantTime", NullValueHandling = NullValueHandling.Ignore)]
    public double? PlantTime { get; set; }

    [JsonProperty("zoneIds", NullValueHandling = NullValueHandling.Ignore)]
    public List<string>? ZoneIds { get; set; }

    [JsonProperty("isCompleted", NullValueHandling = NullValueHandling.Ignore)]
    public bool? IsCompleted { get; set; }

    [JsonProperty("weapon", NullValueHandling = NullValueHandling.Ignore)]
    public List<string>? Weapon { get; set; }

    [JsonProperty("distance", NullValueHandling = NullValueHandling.Ignore)]
    public NativeConditionDistance? Distance { get; set; }

    [JsonProperty("weaponModsInclusive", NullValueHandling = NullValueHandling.Ignore)]
    public List<List<string>>? WeaponModsInclusive { get; set; }

    [JsonProperty("weaponModsExclusive", NullValueHandling = NullValueHandling.Ignore)]
    public List<List<string>>? WeaponModsExclusive { get; set; }

    [JsonProperty("enemyEquipmentInclusive", NullValueHandling = NullValueHandling.Ignore)]
    public List<List<string>>? EnemyEquipmentInclusive { get; set; }

    [JsonProperty("enemyEquipmentExclusive", NullValueHandling = NullValueHandling.Ignore)]
    public List<List<string>>? EnemyEquipmentExclusive { get; set; }

    [JsonProperty("weaponCaliber", NullValueHandling = NullValueHandling.Ignore)]
    public List<string>? WeaponCaliber { get; set; }

    [JsonProperty("savageRole", NullValueHandling = NullValueHandling.Ignore)]
    public List<string>? SavageRole { get; set; }

    [JsonProperty("bodyPart", NullValueHandling = NullValueHandling.Ignore)]
    public List<string>? BodyPart { get; set; }

    [JsonProperty("daytime", NullValueHandling = NullValueHandling.Ignore)]
    public NativeConditionDaytime? Daytime { get; set; }

    [JsonProperty("enemyHealthEffects", NullValueHandling = NullValueHandling.Ignore)]
    public List<NativeHealthEffect>? EnemyHealthEffects { get; set; }

    [JsonProperty("resetOnSessionEnd", NullValueHandling = NullValueHandling.Ignore)]
    public bool? ResetOnSessionEnd { get; set; }

    [JsonProperty("equipmentInclusive", NullValueHandling = NullValueHandling.Ignore)]
    public List<List<string>>? EquipmentInclusive { get; set; }

    [JsonProperty("equipmentExclusive", NullValueHandling = NullValueHandling.Ignore)]
    public List<List<string>>? EquipmentExclusive { get; set; }

    [JsonProperty("IncludeNotEquippedItems", NullValueHandling = NullValueHandling.Ignore)]
    public bool? IncludeNotEquippedItems { get; set; }

    [JsonProperty("zoneId", NullValueHandling = NullValueHandling.Ignore)]
    public string? ZoneId { get; set; }

    [JsonProperty("traderId")]
    public string? TraderId { get; set; }

    [JsonProperty("areaType")]
    public int? AreaType { get; set; }

    [JsonProperty("exitName")]
    public string? ExitName { get; set; }

    [JsonProperty("containsItems")]
    public List<string>? ContainsItems { get; set; }

    [JsonProperty("hasItemFromCategory")]
    public List<string>? HasItemFromCategory { get; set; }

    [JsonProperty("bodyPartsWithEffects")]
    public List<NativeHealthEffect>? BodyPartsWithEffects { get; set; }

    [JsonProperty("baseAccuracy")]
    public NativeConditionDistance? BaseAccuracy { get; set; }

    [JsonProperty("durability")]
    public NativeConditionDistance? Durability { get; set; }

    [JsonProperty("effectiveDistance")]
    public NativeConditionDistance? EffectiveDistance { get; set; }

    [JsonProperty("emptyTacticalSlot")]
    public NativeConditionDistance? EmptyTacticalSlot { get; set; }

    [JsonProperty("ergonomics")]
    public NativeConditionDistance? Ergonomics { get; set; }

    [JsonProperty("height")]
    public NativeConditionDistance? Height { get; set; }

    [JsonProperty("magazineCapacity")]
    public NativeConditionDistance? MagazineCapacity { get; set; }

    [JsonProperty("muzzleVelocity")]
    public NativeConditionDistance? MuzzleVelocity { get; set; }

    [JsonProperty("recoil")]
    public NativeConditionDistance? Recoil { get; set; }

    [JsonProperty("weight")]
    public NativeConditionDistance? Weight { get; set; }

    [JsonProperty("width")]
    public NativeConditionDistance? Width { get; set; }

    [JsonProperty("energy")]
    public NativeConditionDistance? Energy { get; set; }

    [JsonProperty("hydration")]
    public NativeConditionDistance? Hydration { get; set; }

    [JsonProperty("time")]
    public NativeConditionDistance? Time { get; set; }
}
