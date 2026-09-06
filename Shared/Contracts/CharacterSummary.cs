namespace SeasonalPerks.Shared.Contracts;

public sealed class CharacterSummary<TVisual>
    where TVisual : class
{
    public string Mode { get; set; } = "normal";
    public string Name { get; set; } = "";
    public int Level { get; set; }
    public bool Exists { get; set; }
    public string Side { get; set; } = "Usec";
    public TVisual? Visual { get; set; }
}
