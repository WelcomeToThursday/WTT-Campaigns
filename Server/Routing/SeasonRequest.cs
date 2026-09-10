using SPTarkov.Server.Core.Models.Utils;
using WTT.Campaigns.Shared.Contracts;

namespace WTT.Campaigns.Server.Routing;

public record SeasonRequest : IRequestData
{
    public int ProtocolVersion { get; set; }
    public string SeasonId { get; set; } = "";
    public string CharacterId { get; set; } = "";
    public string OperationId { get; set; } = "";
    public long ExpectedRevision { get; set; }
    public List<string> PerkIds { get; set; } = [];
    public string Mode { get; set; } = "normal";
    public string Nickname { get; set; } = "Campaign";
    public string Side { get; set; } = "Usec";
    public string HeadId { get; set; } = "";
    public string VoiceId { get; set; } = "";

    public Mutation ToMutation()
    {
        return new()
        {
            ProtocolVersion = ProtocolVersion,
            SeasonId = SeasonId,
            CharacterId = CharacterId,
            OperationId = OperationId,
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
