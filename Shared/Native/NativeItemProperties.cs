using Newtonsoft.Json;
using SeasonalPerks.Shared.Serialization;

namespace SeasonalPerks.Shared.Native;

public sealed class NativeItemProperties : NativeModel
{
    [JsonProperty("AnimationVariantsNumber", NullValueHandling = NullValueHandling.Ignore)]
    public int? AnimationVariantsNumber { get; set; }

    [JsonProperty("BackgroundColor", NullValueHandling = NullValueHandling.Ignore)]
    public string? BackgroundColor { get; set; }

    [JsonProperty("CanRequireOnRagfair", NullValueHandling = NullValueHandling.Ignore)]
    public bool? CanRequireOnRagfair { get; set; }

    [JsonProperty("CanSellOnRagfair", NullValueHandling = NullValueHandling.Ignore)]
    public bool? CanSellOnRagfair { get; set; }

    [JsonProperty("ConflictingItems", NullValueHandling = NullValueHandling.Ignore)]
    public List<string>? ConflictingItems { get; set; }

    [JsonProperty("Description", NullValueHandling = NullValueHandling.Ignore)]
    public string? Description { get; set; }

    [JsonProperty("DiscardLimit", NullValueHandling = NullValueHandling.Ignore)]
    public int? DiscardLimit { get; set; }

    [JsonProperty("DiscardingBlock", NullValueHandling = NullValueHandling.Ignore)]
    public bool? DiscardingBlock { get; set; }

    [JsonProperty("ExamineExperience", NullValueHandling = NullValueHandling.Ignore)]
    public int? ExamineExperience { get; set; }

    [JsonProperty("ExamineTime", NullValueHandling = NullValueHandling.Ignore)]
    public int? ExamineTime { get; set; }

    [JsonProperty("ExaminedByDefault", NullValueHandling = NullValueHandling.Ignore)]
    public bool? ExaminedByDefault { get; set; }

    [JsonProperty("ExtraSizeDown", NullValueHandling = NullValueHandling.Ignore)]
    public int? ExtraSizeDown { get; set; }

    [JsonProperty("ExtraSizeForceAdd", NullValueHandling = NullValueHandling.Ignore)]
    public bool? ExtraSizeForceAdd { get; set; }

    [JsonProperty("ExtraSizeLeft", NullValueHandling = NullValueHandling.Ignore)]
    public int? ExtraSizeLeft { get; set; }

    [JsonProperty("ExtraSizeRight", NullValueHandling = NullValueHandling.Ignore)]
    public int? ExtraSizeRight { get; set; }

    [JsonProperty("ExtraSizeUp", NullValueHandling = NullValueHandling.Ignore)]
    public int? ExtraSizeUp { get; set; }

    [JsonProperty("Height", NullValueHandling = NullValueHandling.Ignore)]
    public int? Height { get; set; }

    [JsonProperty("HideEntrails", NullValueHandling = NullValueHandling.Ignore)]
    public bool? HideEntrails { get; set; }

    [JsonProperty("InsuranceDisabled", NullValueHandling = NullValueHandling.Ignore)]
    public bool? InsuranceDisabled { get; set; }

    [JsonProperty("IsAlwaysAvailableForInsurance", NullValueHandling = NullValueHandling.Ignore)]
    public bool? IsAlwaysAvailableForInsurance { get; set; }

    [JsonProperty("IsLockedafterEquip", NullValueHandling = NullValueHandling.Ignore)]
    public bool? IsLockedafterEquip { get; set; }

    [JsonProperty("IsNotDeletableFromQuestStashAfterQuestComplete", NullValueHandling = NullValueHandling.Ignore)]
    public bool? IsNotDeletableFromQuestStashAfterQuestComplete { get; set; }

    [JsonProperty("IsSecretExitRequirement", NullValueHandling = NullValueHandling.Ignore)]
    public bool? IsSecretExitRequirement { get; set; }

    [JsonProperty("IsSpecialSlotOnly", NullValueHandling = NullValueHandling.Ignore)]
    public bool? IsSpecialSlotOnly { get; set; }

    [JsonProperty("IsUnbuyable", NullValueHandling = NullValueHandling.Ignore)]
    public bool? IsUnbuyable { get; set; }

    [JsonProperty("IsUndiscardable", NullValueHandling = NullValueHandling.Ignore)]
    public bool? IsUndiscardable { get; set; }

    [JsonProperty("IsUngivable", NullValueHandling = NullValueHandling.Ignore)]
    public bool? IsUngivable { get; set; }

    [JsonProperty("IsUnremovable", NullValueHandling = NullValueHandling.Ignore)]
    public bool? IsUnremovable { get; set; }

    [JsonProperty("IsUnsaleable", NullValueHandling = NullValueHandling.Ignore)]
    public bool? IsUnsaleable { get; set; }

    [JsonProperty("ItemSound", NullValueHandling = NullValueHandling.Ignore)]
    public string? ItemSound { get; set; }

    [JsonProperty("LeftHandItem", NullValueHandling = NullValueHandling.Ignore)]
    public bool? LeftHandItem { get; set; }

    [JsonProperty("LootExperience", NullValueHandling = NullValueHandling.Ignore)]
    public int? LootExperience { get; set; }

    [JsonProperty("MergesWithChildren", NullValueHandling = NullValueHandling.Ignore)]
    public bool? MergesWithChildren { get; set; }

    [JsonProperty("Name", NullValueHandling = NullValueHandling.Ignore)]
    public string? Name { get; set; }

    [JsonProperty("NotShownInSlot", NullValueHandling = NullValueHandling.Ignore)]
    public bool? NotShownInSlot { get; set; }

    [JsonProperty("Prefab", NullValueHandling = NullValueHandling.Ignore)]
    public NativeItemPropertiesPrefab? Prefab { get; set; }

    [JsonProperty("QuestItem", NullValueHandling = NullValueHandling.Ignore)]
    public bool? QuestItem { get; set; }

    [JsonProperty("QuestStashMaxCount", NullValueHandling = NullValueHandling.Ignore)]
    public int? QuestStashMaxCount { get; set; }

    [JsonProperty("RagFairCommissionModifier", NullValueHandling = NullValueHandling.Ignore)]
    public int? RagFairCommissionModifier { get; set; }

    [JsonProperty("RagfairLevelToTrade", NullValueHandling = NullValueHandling.Ignore)]
    public int? RagfairLevelToTrade { get; set; }

    [JsonProperty("RarityPvE", NullValueHandling = NullValueHandling.Ignore)]
    public string? RarityPvE { get; set; }

    [JsonProperty("RepairCost", NullValueHandling = NullValueHandling.Ignore)]
    public int? RepairCost { get; set; }

    [JsonProperty("RepairSpeed", NullValueHandling = NullValueHandling.Ignore)]
    public int? RepairSpeed { get; set; }

    [JsonProperty("ShortName", NullValueHandling = NullValueHandling.Ignore)]
    public string? ShortName { get; set; }

    [JsonProperty("StackMaxSize", NullValueHandling = NullValueHandling.Ignore)]
    public int? StackMaxSize { get; set; }

    [JsonProperty("StackObjectsCount", NullValueHandling = NullValueHandling.Ignore)]
    public int? StackObjectsCount { get; set; }

    [JsonProperty("Unlootable", NullValueHandling = NullValueHandling.Ignore)]
    public bool? Unlootable { get; set; }

    [JsonProperty("UnlootableFromSide", NullValueHandling = NullValueHandling.Ignore)]
    public List<string>? UnlootableFromSide { get; set; }

    [JsonProperty("UnlootableFromSlot", NullValueHandling = NullValueHandling.Ignore)]
    public string? UnlootableFromSlot { get; set; }

    [JsonProperty("UsePrefab", NullValueHandling = NullValueHandling.Ignore)]
    public NativeItemPropertiesUsePrefab? UsePrefab { get; set; }

    [JsonProperty("Weight", NullValueHandling = NullValueHandling.Ignore)]
    public double? Weight { get; set; }

    [JsonProperty("Width", NullValueHandling = NullValueHandling.Ignore)]
    public int? Width { get; set; }

    [JsonProperty("StackMaxRandom", NullValueHandling = NullValueHandling.Ignore)]
    public int? StackMaxRandom { get; set; }

    [JsonProperty("StackMinRandom", NullValueHandling = NullValueHandling.Ignore)]
    public int? StackMinRandom { get; set; }

    [JsonProperty("Grids", NullValueHandling = NullValueHandling.Ignore)]
    public List<NativeItemPropertiesGrids>? Grids { get; set; }

    [JsonProperty("Slots", NullValueHandling = NullValueHandling.Ignore)]
    public List<string>? Slots { get; set; }

    [JsonProperty("CanPutIntoDuringTheRaid", NullValueHandling = NullValueHandling.Ignore)]
    public bool? CanPutIntoDuringTheRaid { get; set; }

    [JsonProperty("CantRemoveFromSlotsDuringRaid", NullValueHandling = NullValueHandling.Ignore)]
    public List<string>? CantRemoveFromSlotsDuringRaid { get; set; }

    [JsonProperty("ForbidMissingVitalParts", NullValueHandling = NullValueHandling.Ignore)]
    public bool? ForbidMissingVitalParts { get; set; }

    [JsonProperty("ForbidNonEmptyContainers", NullValueHandling = NullValueHandling.Ignore)]
    public bool? ForbidNonEmptyContainers { get; set; }

    [JsonProperty("BlocksArmorVest", NullValueHandling = NullValueHandling.Ignore)]
    public bool? BlocksArmorVest { get; set; }

    [JsonProperty("SearchSound", NullValueHandling = NullValueHandling.Ignore)]
    public string? SearchSound { get; set; }
}
