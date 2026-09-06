namespace SeasonalPerks.Shared.Contracts;

public sealed class Mutation
{
    public int ProtocolVersion { get; set; } = 2;
    public string SeasonId { get; set; } = "";
    public string CharacterId { get; set; } = "";
    public string OperationId { get; set; } = "";
    public long ExpectedRevision { get; set; }
    public List<string> PerkIds { get; set; } = new();
    public string Mode { get; set; } = "normal";
    public string Nickname { get; set; } = "Seasonal";
    public string Side { get; set; } = "Usec";
    public string HeadId { get; set; } = "";
    public string VoiceId { get; set; } = "";
}
