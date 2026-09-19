using Newtonsoft.Json;
using WTT.Campaigns.Shared.Native;
using WTT.Campaigns.Shared.Serialization;

namespace WTT.Campaigns.Shared.Seasons;

public sealed class CampaignQuestLoot
{
    public string QuestId { get; set; } = "";
    public string ItemTemplate { get; set; } = "";
    public List<string> BotRoles { get; set; } = new();
}

public sealed class CampaignCraft : ExtensibleJsonModel
{
    [JsonProperty("_id")]
    public string Id { get; set; } = "";

    [JsonProperty("areaType")]
    public int AreaType { get; set; }

    [JsonProperty("requirements")]
    public List<CampaignCraftRequirement> Requirements { get; set; } = new();

    [JsonProperty("productionTime")]
    public double ProductionTime { get; set; } = 900;

    [JsonProperty("endProduct")]
    public string EndProduct { get; set; } = "";

    [JsonProperty("count")]
    public int Count { get; set; } = 1;

    [JsonProperty("locked")]
    public bool Locked { get; set; } = true;
}

public sealed class CampaignCraftRequirement : ExtensibleJsonModel
{
    [JsonProperty("type")]
    public string Type { get; set; } = "";

    [JsonProperty("questId", NullValueHandling = NullValueHandling.Ignore)]
    public string? QuestId { get; set; }

    [JsonProperty("templateId", NullValueHandling = NullValueHandling.Ignore)]
    public string? TemplateId { get; set; }

    [JsonProperty("count", NullValueHandling = NullValueHandling.Ignore)]
    public int? Count { get; set; }

    [JsonProperty("areaType", NullValueHandling = NullValueHandling.Ignore)]
    public int? AreaType { get; set; }

    [JsonProperty("requiredLevel", NullValueHandling = NullValueHandling.Ignore)]
    public int? RequiredLevel { get; set; }
}

public static class CampaignQuestResources
{
    public static void Validate(SeasonDefinition season, SeasonValidationResult result)
    {
        var quests = season.Quests.AsValueEnumerable().Where(q => q.SeasonalEnabled != false).ToDictionary(q => q.Id);
        foreach (var loot in season.QuestLoot)
        {
            if (
                !quests.ContainsKey(loot.QuestId)
                || !SeasonValidator.IsId(loot.ItemTemplate)
                || loot.BotRoles.Count == 0
                || loot.BotRoles.AsValueEnumerable().Any(string.IsNullOrWhiteSpace)
            )
                result.Add("Quest loot", "Quest loot requires an active owned quest, item template, and explicit bot roles.");
        }
        var ids = new HashSet<string>();
        foreach (var craft in season.Crafts)
        {
            var path = "Crafts/" + craft.Id;
            if (
                !SeasonValidator.IsId(craft.Id)
                || !ids.Add(craft.Id)
                || !SeasonValidator.IsId(craft.EndProduct)
                || craft.Count is < 1 or > 100000
                || craft.ProductionTime <= 0
                || double.IsNaN(craft.ProductionTime)
                || double.IsInfinity(craft.ProductionTime)
                || craft.AreaType is < 0 or > 100
                || !craft.Locked
            )
                result.Add(path, "Campaign crafts require a unique identity, item, positive time and quantity, and a quest lock.");
            var gates = craft.Requirements.AsValueEnumerable().Where(r => r.Type == "QuestComplete").ToArray();
            if (
                gates.Length != 1
                || !quests.TryGetValue(gates[0].QuestId ?? "", out var quest)
                || !quest
                    .AllRewards()
                    .AsValueEnumerable()
                    .Any(r => r.Type == "ProductionScheme" && r.Items.AsValueEnumerable().Any(i => i.Template == craft.EndProduct))
            )
                result.Add(path, "A craft needs one active owned quest with its matching production reward.");
            foreach (var requirement in craft.Requirements)
            {
                if (
                    requirement.Type switch
                    {
                        "QuestComplete" => !quests.ContainsKey(requirement.QuestId ?? ""),
                        "Area" => requirement.AreaType != craft.AreaType || requirement.RequiredLevel is not (>= 1 and <= 10),
                        "Item" => !SeasonValidator.IsId(requirement.TemplateId) || requirement.Count is not (>= 1 and <= 100000),
                        _ => true,
                    }
                )
                    result.Add(path, "Unsupported or malformed craft requirement.");
            }
        }
        foreach (var quest in quests.Values.AsValueEnumerable().Where(_ => !season.Legacy))
        foreach (var reward in quest.AllRewards().AsValueEnumerable().Where(r => r.Type == "ProductionScheme"))
            if (
                !season
                    .Crafts.AsValueEnumerable()
                    .Any(c =>
                        c.Requirements.AsValueEnumerable().Any(r => r.QuestId == quest.Id)
                        && reward.Items.AsValueEnumerable().Any(i => i.Template == c.EndProduct)
                    )
            )
                result.Add("Quests/" + quest.Id, "Production reward has no owned quest-unlocked recipe.");
    }
}
