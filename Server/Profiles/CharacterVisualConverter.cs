using Newtonsoft.Json;
using SPTarkov.Server.Core.Utils;

namespace SeasonalPerks.Server.Profiles;

// SPT's item and customization contracts use its System.Text.Json converters.
// Keep those converters when embedding an appearance in our Newtonsoft response.
internal sealed class CharacterVisualConverter(JsonUtil json) : JsonConverter<CharacterVisual>
{
    public override bool CanRead
    {
        get { return false; }
    }

    public override void WriteJson(JsonWriter writer, CharacterVisual? value, JsonSerializer serializer)
    {
        writer.WriteRawValue(json.Serialize(value));
    }

    public override CharacterVisual ReadJson(
        JsonReader reader,
        Type objectType,
        CharacterVisual? existingValue,
        bool hasExistingValue,
        JsonSerializer serializer
    )
    {
        throw new NotSupportedException();
    }
}
