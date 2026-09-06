using Newtonsoft.Json;
using SeasonalPerks.Shared;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Utils;

namespace SeasonalPerks.Server;

public sealed class ServerSnapshot : Snapshot<CharacterVisual>;

public sealed class CharacterVisual
{
    public CharacterVisualInfo Info { get; set; } = new();
    public Customization? Customization { get; set; }
    public CharacterEquipment Equipment { get; set; } = new();
}

public sealed class CharacterVisualInfo
{
    public string? Nickname { get; set; }
    public int? Level { get; set; }
    public string? Side { get; set; }
}

public sealed class CharacterEquipment
{
    public MongoId? Id { get; set; }
    public Item[] Items { get; set; } = [];
}

// SPT's item and customization contracts use its System.Text.Json converters.
// Keep those converters when embedding an appearance in our Newtonsoft response.
internal sealed class CharacterVisualConverter(JsonUtil json) : JsonConverter<CharacterVisual>
{
    public override bool CanRead => false;

    public override void WriteJson(
        JsonWriter writer,
        CharacterVisual? value,
        JsonSerializer serializer
    ) => writer.WriteRawValue(json.Serialize(value));

    public override CharacterVisual? ReadJson(
        JsonReader reader,
        Type objectType,
        CharacterVisual? existingValue,
        bool hasExistingValue,
        JsonSerializer serializer
    ) => throw new NotSupportedException();
}
