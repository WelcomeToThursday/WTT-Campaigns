using SPTarkov.Server.Core.Models.Utils;

namespace WTT.Campaigns.Server.Hub;

public class HubRequest : WTT.Campaigns.Shared.Contracts.HubMutation, IRequestData
{
    public string CharacterId { get; set; } = "";
}
