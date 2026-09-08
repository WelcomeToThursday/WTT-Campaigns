namespace SeasonalPerks.Shared.Story;

// Constructed by the authority from the active profile; never accepted from a mutation request.
public sealed class StoryFacts
{
    public bool InRaid { get; set; }
    public int Level { get; set; }
    public string Location { get; set; } = "";
    public string TraderId { get; set; } = "";
    public int FreeSpecialSlots { get; set; }
    public Dictionary<string, string> QuestStatuses { get; set; } = new();
    public HashSet<string> CompletedConditions { get; set; } = new();
    public Dictionary<string, double> ConditionCounters { get; set; } = new();
    public Dictionary<string, double> TraderReputation { get; set; } = new();
    public Dictionary<string, double> TraderLoyalty { get; set; } = new();
    public Dictionary<string, double> Items { get; set; } = new();
    public Dictionary<string, double> HandoverItems { get; set; } = new();
    public Dictionary<string, double> Skills { get; set; } = new();
    public Dictionary<string, double> HideoutAreas { get; set; } = new();
    public HashSet<string> AvailableServices { get; set; } = new();
    public HashSet<string> TradersWithNewQuests { get; set; } = new();
    public Dictionary<string, int> SessionVariables { get; set; } = new();
}
