using Newtonsoft.Json;
using SeasonalPerks.Shared.Serialization;

namespace SeasonalPerks.Shared.Profiles;

public sealed class AllergyTargets : ExtensibleJsonModel
{
    [JsonProperty("targetItems")]
    public List<string>? TargetItems { get; set; } = new();

    internal AllergyTargets DeepClone()
    {
        var copy = new AllergyTargets { TargetItems = TargetItems?.ToList() };
        CopyExtraTo(copy);
        return copy;
    }
}
