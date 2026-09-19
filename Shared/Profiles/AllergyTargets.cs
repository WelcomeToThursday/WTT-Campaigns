using Newtonsoft.Json;
using WTT.Campaigns.Shared.Serialization;

namespace WTT.Campaigns.Shared.Profiles;

public sealed class AllergyTargets : ExtensibleJsonModel
{
    [JsonProperty("targetItems")]
    public List<string>? TargetItems { get; set; } = new();

    internal AllergyTargets DeepClone()
    {
        var copy = new AllergyTargets { TargetItems = TargetItems?.AsValueEnumerable().ToList() };
        CopyExtraTo(copy);
        return copy;
    }
}
