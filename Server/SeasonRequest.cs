using Newtonsoft.Json;
using SeasonalPerks.Shared;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils;

namespace SeasonalPerks.Server;

public record SeasonRequest : IRequestData
{
    public long ExpectedRevision { get; set; }
    public List<string> PerkIds { get; set; } = [];
    public string Mode { get; set; } = "normal";
    public string Nickname { get; set; } = "Seasonal";
    public string Side { get; set; } = "Usec";
    public string HeadId { get; set; } = "";
    public string VoiceId { get; set; } = "";

    public Mutation ToMutation() =>
        new()
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
