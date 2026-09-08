namespace SeasonalPerks.Shared.Contracts;

public sealed class CharacterSummary<TVisual>
    where TVisual : class
{
    public string Mode { get; set; } = "normal";
    public string CreationOperationId { get; set; } = "";
    public string Id { get; set; } = "";
    public string SeasonId { get; set; } = "";
    public string SeasonName { get; set; } = "";
    public bool Available { get; set; } = true;
    public string Name { get; set; } = "";
    public int Level { get; set; }
    public string StoryChapters { get; set; } = "";
    public bool Exists { get; set; }
    public bool Wiped { get; set; }
    public string Side { get; set; } = "Usec";
    public TVisual? Visual { get; set; }
}
