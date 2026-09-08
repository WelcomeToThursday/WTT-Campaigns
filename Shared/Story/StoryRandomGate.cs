namespace SeasonalPerks.Shared.Story;

public sealed class StoryRandomGate
{
    public string VariableId { get; set; } = "";
    public int Start { get; set; }
    public int End { get; set; }
    public int Maximum { get; set; }
    public int Group { get; set; }
}
