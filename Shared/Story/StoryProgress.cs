namespace WTT.Campaigns.Shared.Story;

public sealed class StoryProgress
{
    public int Version { get; set; } = 1;
    public string SeasonId { get; set; } = "";
    public HashSet<string> UnlockedMissionLinks { get; set; } = new();
    public long Revision { get; set; }
    public Dictionary<string, int> Variables { get; set; } = new();
    public Dictionary<string, long> Notes { get; set; } = new();
    public HashSet<string> ReadNotes { get; set; } = new();
    public HashSet<string> ReadConditions { get; set; } = new();
    public HashSet<string> ReadLinks { get; set; } = new();
    public HashSet<string> CompletedItems { get; set; } = new();
    public HashSet<string> CompletedBindings { get; set; } = new();
    public Dictionary<string, StoryReceipt> Receipts { get; set; } = new();
    public StoryConversation? Conversation { get; set; }
    public StoryRaid? Raid { get; set; }
}
