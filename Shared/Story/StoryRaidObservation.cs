namespace SeasonalPerks.Shared.Story;

// Ephemeral local raid facts. Never merge this projection into the saved PMC.
public sealed class StoryRaidObservation
{
    public string CharacterId { get; set; } = "";
    public string RaidId { get; set; } = "";
    public long Sequence { get; set; }
    public int Level { get; set; }
    public int FreeSpecialSlots { get; set; }
    public List<StoryObservedItem> Items { get; set; } = new();
    public Dictionary<string, double> Counters { get; set; } = new();
    public HashSet<string> CompletedConditions { get; set; } = new();
    public Dictionary<string, double> Skills { get; set; } = new();
}
