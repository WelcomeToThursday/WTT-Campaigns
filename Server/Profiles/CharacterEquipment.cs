using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace WTT.Campaigns.Server.Profiles;

public sealed class CharacterEquipment
{
    public MongoId? Id { get; set; }
    public Item[] Items { get; set; } = [];
}
