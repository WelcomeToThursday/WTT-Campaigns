using SPTarkov.Server.Core.Models.Utils;

namespace SeasonalPerks.Server.Hub;

public class HubRequest : SeasonalPerks.Shared.Contracts.HubMutation, IRequestData
{
    public string CharacterId { get; set; } = "";
}
