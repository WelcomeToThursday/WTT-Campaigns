using SeasonalPerks.Shared.Contracts;
using SPTarkov.Server.Core.Models.Utils;

namespace SeasonalPerks.Server.Routing;

public record SeasonRequest : IRequestData
{
    public int ProtocolVersion { get; set; }
    public string SeasonId { get; set; } = "";
    public long ExpectedRevision { get; set; }
    public List<string> PerkIds { get; set; } = [];
    public string Mode { get; set; } = "normal";
    public string Nickname { get; set; } = "Seasonal";
    public string Side { get; set; } = "Usec";
    public string HeadId { get; set; } = "";
    public string VoiceId { get; set; } = "";

    public Mutation ToMutation()
    {
        return new()
        {
            ExpectedRevision = ExpectedRevision,
            PerkIds = PerkIds,
            Mode = Mode,
            Nickname = Nickname,
            Side = Side,
            HeadId = HeadId,
            VoiceId = VoiceId,
        };
    }
}
