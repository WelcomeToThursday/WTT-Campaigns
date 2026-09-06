using Newtonsoft.Json;
using SeasonalPerks.Shared.Profiles;

namespace SeasonalPerks.Shared.Serialization;

/// <summary>Older captures encode an empty allergy map as an array.</summary>
internal sealed class AllergyParametersConverter : JsonConverter<Dictionary<string, AllergyTargets>>
{
    public override bool CanWrite
    {
        get { return false; }
    }

    public override Dictionary<string, AllergyTargets>? ReadJson(
        JsonReader reader,
        Type objectType,
        Dictionary<string, AllergyTargets>? existingValue,
        bool hasExistingValue,
        JsonSerializer serializer
    )
    {
        if (reader.TokenType == JsonToken.StartArray)
        {
            reader.Skip();
            return new();
        }
        return serializer.Deserialize<Dictionary<string, AllergyTargets>>(reader);
    }

    public override void WriteJson(JsonWriter writer, Dictionary<string, AllergyTargets>? value, JsonSerializer serializer)
    {
        throw new NotSupportedException();
    }
}
