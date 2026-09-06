using Newtonsoft.Json;

namespace SeasonalPerks.Shared;

public sealed class EffectParameters : ExtensibleJsonModel
{
    [JsonProperty("allergy", NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(AllergyParametersConverter))]
    public Dictionary<string, AllergyTargets>? Allergy { get; set; }

    public EffectParameters DeepClone()
    {
        var copy = new EffectParameters
        {
            Allergy = Allergy?.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone()!),
        };
        CopyExtraTo(copy);
        return copy;
    }
}

public sealed class AllergyTargets : ExtensibleJsonModel
{
    [JsonProperty("targetItems")]
    public List<string> TargetItems { get; set; } = new();

    internal AllergyTargets DeepClone()
    {
        var copy = new AllergyTargets { TargetItems = TargetItems?.ToList()! };
        CopyExtraTo(copy);
        return copy;
    }
}

/// <summary>Older captures encode an empty allergy map as an array.</summary>
internal sealed class AllergyParametersConverter : JsonConverter<Dictionary<string, AllergyTargets>>
{
    public override bool CanWrite => false;

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

    public override void WriteJson(
        JsonWriter writer,
        Dictionary<string, AllergyTargets>? value,
        JsonSerializer serializer
    ) => throw new NotSupportedException();
}
