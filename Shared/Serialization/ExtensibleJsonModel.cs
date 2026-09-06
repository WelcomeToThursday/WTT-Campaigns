using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SeasonalPerks.Shared.Serialization;

/// <summary>Retains unrecognized fields at the serialization boundary for forward compatibility.</summary>
public abstract class ExtensibleJsonModel
{
    [JsonExtensionData]
    private IDictionary<string, JToken>? _extra;

    protected void CopyExtraTo(ExtensibleJsonModel copy) =>
        copy._extra = _extra?.ToDictionary(pair => pair.Key, pair => pair.Value.DeepClone());
}
