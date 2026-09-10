using Newtonsoft.Json;
using WTT.Campaigns.Shared.Serialization;

namespace WTT.Campaigns.Shared.Native;

public sealed class NativeQuest : NativeModel
{
    [JsonProperty("_id", NullValueHandling = NullValueHandling.Ignore)]
    public string Id { get; set; } = "";

    [JsonProperty("traderId", NullValueHandling = NullValueHandling.Ignore)]
    public string? TraderId { get; set; }

    [JsonProperty("location", NullValueHandling = NullValueHandling.Ignore)]
    public string? Location { get; set; }

    [JsonProperty("image", NullValueHandling = NullValueHandling.Ignore)]
    public string? Image { get; set; }

    [JsonProperty("type", NullValueHandling = NullValueHandling.Ignore)]
    public string? Type { get; set; }

    [JsonProperty("isKey", NullValueHandling = NullValueHandling.Ignore)]
    public bool? IsKey { get; set; }

    [JsonProperty("restartable", NullValueHandling = NullValueHandling.Ignore)]
    public bool? Restartable { get; set; }

    [JsonProperty("instantComplete", NullValueHandling = NullValueHandling.Ignore)]
    public bool? InstantComplete { get; set; }

    [JsonProperty("secretQuest", NullValueHandling = NullValueHandling.Ignore)]
    public bool? SecretQuest { get; set; }

    [JsonProperty("notDisplayedQuest", NullValueHandling = NullValueHandling.Ignore)]
    public bool? NotDisplayedQuest { get; set; }

    [JsonProperty("canShowNotificationsInGame", NullValueHandling = NullValueHandling.Ignore)]
    public bool? CanShowNotificationsInGame { get; set; }

    [JsonProperty("rewards", NullValueHandling = NullValueHandling.Ignore)]
    public Dictionary<string, List<NativeReward>> Rewards { get; set; } = new();

    [JsonProperty("conditions", NullValueHandling = NullValueHandling.Ignore)]
    public NativeQuestConditions Conditions { get; set; } = new();

    [JsonProperty("side", NullValueHandling = NullValueHandling.Ignore)]
    public string? Side { get; set; }

    [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)]
    public string? Name { get; set; }

    [JsonProperty("note", NullValueHandling = NullValueHandling.Ignore)]
    public string? Note { get; set; }

    [JsonProperty("description", NullValueHandling = NullValueHandling.Ignore)]
    public string? Description { get; set; }

    [JsonProperty("successMessageText", NullValueHandling = NullValueHandling.Ignore)]
    public string? SuccessMessageText { get; set; }

    [JsonProperty("failMessageText", NullValueHandling = NullValueHandling.Ignore)]
    public string? FailMessageText { get; set; }

    [JsonProperty("startedMessageText", NullValueHandling = NullValueHandling.Ignore)]
    public string? StartedMessageText { get; set; }

    [JsonProperty("changeQuestMessageText", NullValueHandling = NullValueHandling.Ignore)]
    public string? ChangeQuestMessageText { get; set; }

    [JsonProperty("acceptPlayerMessage", NullValueHandling = NullValueHandling.Ignore)]
    public string? AcceptPlayerMessage { get; set; }

    [JsonProperty("declinePlayerMessage", NullValueHandling = NullValueHandling.Ignore)]
    public string? DeclinePlayerMessage { get; set; }

    [JsonProperty("completePlayerMessage", NullValueHandling = NullValueHandling.Ignore)]
    public string? CompletePlayerMessage { get; set; }

    [JsonProperty("acceptanceAndFinishingSource", NullValueHandling = NullValueHandling.Ignore)]
    public string? AcceptanceAndFinishingSource { get; set; }

    [JsonProperty("progressSource", NullValueHandling = NullValueHandling.Ignore)]
    public string? ProgressSource { get; set; }

    [JsonProperty("rankingModes", NullValueHandling = NullValueHandling.Ignore)]
    public List<string>? RankingModes { get; set; }

    [JsonProperty("gameModes", NullValueHandling = NullValueHandling.Ignore)]
    public List<string>? GameModes { get; set; }

    [JsonProperty("arenaLocations", NullValueHandling = NullValueHandling.Ignore)]
    public List<string>? ArenaLocations { get; set; }

    [JsonProperty("localization", NullValueHandling = NullValueHandling.Ignore)]
    public Dictionary<string, Dictionary<string, string>> Localization { get; set; } = new();

    [JsonProperty("dialogueId", NullValueHandling = NullValueHandling.Ignore)]
    public string? DialogueId { get; set; }

    [JsonProperty("mailSettings", NullValueHandling = NullValueHandling.Ignore)]
    public NativeQuestMailSettings? MailSettings { get; set; }

    [JsonProperty("isStoryQuest", NullValueHandling = NullValueHandling.Ignore)]
    public bool? IsStoryQuest { get; set; }

    [JsonProperty("notes", NullValueHandling = NullValueHandling.Ignore)]
    public NativeQuestNotes? Notes { get; set; }

    [JsonProperty("inBufferZoneOnly", NullValueHandling = NullValueHandling.Ignore)]
    public bool? InBufferZoneOnly { get; set; }

    [JsonProperty("icon", NullValueHandling = NullValueHandling.Ignore)]
    public string? Icon { get; set; }

    [JsonProperty("tierAccessory", NullValueHandling = NullValueHandling.Ignore)]
    public int? TierAccessory { get; set; }

    [JsonProperty("rewardsInfo", NullValueHandling = NullValueHandling.Ignore)]
    public List<NativeQuestRewardsInfo>? RewardsInfo { get; set; }

    [JsonProperty("QuestName", NullValueHandling = NullValueHandling.Ignore)]
    public string? QuestName { get; set; }

    [JsonProperty("_seasonalEnabled", NullValueHandling = NullValueHandling.Ignore)]
    public bool? SeasonalEnabled { get; set; }
}
