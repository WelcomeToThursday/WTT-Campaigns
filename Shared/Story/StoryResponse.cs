namespace WTT.Campaigns.Shared.Story;

public sealed class StoryResponse
{
    public int Version { get; set; } = 2;
    public string? Error { get; set; }
    public string SeasonId { get; set; } = "";
    public string CharacterId { get; set; } = "";
    public long Revision { get; set; }
    public StoryDefinition? Definition { get; set; }
    public StoryProgress? State { get; set; }
    public StoryFacts? Facts { get; set; }
    public List<StoryDialogLine> Choices { get; set; } = new();
    public List<StoryAction> Presentation { get; set; } = new();
    public StoryDialogLine? Line { get; set; }
    public List<StoryDialogLine> Lines { get; set; } = new();
    public bool Replayed { get; set; }
    public bool NativeProfileChanged { get; set; }
    public string NativeUpdate { get; set; } = "";
    public long NativeRevision { get; set; }
    public List<StoryObjective> Objectives { get; set; } = new();
    public string PreparationId { get; set; } = "";
    public StoryHandover? Handover { get; set; }
    public string EventMediaId { get; set; } = "";
    public string CinematicBindingId { get; set; } = "";
    public List<string> RaidConditionIds { get; set; } = new();
}
