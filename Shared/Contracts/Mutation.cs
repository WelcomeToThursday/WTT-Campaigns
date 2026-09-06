namespace SeasonalPerks.Shared.Contracts;

public sealed class Mutation
{
    public long ExpectedRevision { get; set; }
    public List<string> PerkIds { get; set; } = new();
    public string Mode { get; set; } = "normal";
    public string Nickname { get; set; } = "Seasonal";
    public string Side { get; set; } = "Usec";
    public string HeadId { get; set; } = "";
    public string VoiceId { get; set; } = "";
}
