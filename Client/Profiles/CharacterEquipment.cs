using EFT;

namespace SeasonalPerks.Client.Profiles;

public sealed class CharacterEquipment
{
    public MongoID Id { get; set; }
    public JsonType.FlatItem[] Items { get; set; } = Array.Empty<JsonType.FlatItem>();
}
