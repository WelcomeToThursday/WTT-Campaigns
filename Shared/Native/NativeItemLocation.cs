using Newtonsoft.Json;
using WTT.Campaigns.Shared.Serialization;

namespace WTT.Campaigns.Shared.Native;

/// <summary>An item is placed in either an indexed cartridge slot or a two-dimensional inventory grid.</summary>
[JsonConverter(typeof(NativeItemLocationConverter))]
public sealed class NativeItemLocation
{
    public int? Slot { get; }
    public NativeGridLocation? Grid { get; }

    public NativeItemLocation(int slot) => Slot = slot;

    public NativeItemLocation(NativeGridLocation grid) => Grid = grid;
}

public sealed class NativeGridLocation : NativeModel
{
    [JsonProperty("x")]
    public int? X { get; set; }

    [JsonProperty("y")]
    public int? Y { get; set; }

    [JsonProperty("r")]
    public string? Rotation { get; set; }

    [JsonProperty("isSearched")]
    public bool? IsSearched { get; set; }

    [JsonProperty("rotation")]
    public bool? Rotated { get; set; }
}

public sealed class NativeItemLocationConverter : JsonConverter<NativeItemLocation>
{
    public override NativeItemLocation? ReadJson(
        JsonReader reader,
        Type objectType,
        NativeItemLocation? existingValue,
        bool hasExistingValue,
        JsonSerializer serializer
    ) =>
        reader.TokenType switch
        {
            JsonToken.Null => null,
            JsonToken.Integer => new NativeItemLocation(Convert.ToInt32(reader.Value)),
            JsonToken.StartObject => new NativeItemLocation(serializer.Deserialize<NativeGridLocation>(reader)!),
            _ => throw new JsonSerializationException("An item location must be a slot number or grid position."),
        };

    public override void WriteJson(JsonWriter writer, NativeItemLocation? value, JsonSerializer serializer)
    {
        if (value == null)
            writer.WriteNull();
        else if (value.Slot is { } slot)
            writer.WriteValue(slot);
        else
            serializer.Serialize(writer, value.Grid);
    }
}
