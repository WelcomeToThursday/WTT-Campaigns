using Newtonsoft.Json;
using SeasonalPerks.Shared.Serialization;

namespace SeasonalPerks.Shared.Profiles;

public sealed class EffectParameters : ExtensibleJsonModel
{
    [JsonProperty("allergy", NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(AllergyParametersConverter))]
    public Dictionary<string, AllergyTargets>? Allergy { get; set; }

    public EffectParameters DeepClone()
    {
        var copy = new EffectParameters { Allergy = Allergy?.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone()!) };
        CopyExtraTo(copy);
        return copy;
    }
}
