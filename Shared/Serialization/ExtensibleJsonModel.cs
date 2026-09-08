using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SeasonalPerks.Shared.Serialization;

/// <summary>Retains unrecognized fields at the serialization boundary for forward compatibility.</summary>
public abstract class ExtensibleJsonModel
{
    [JsonExtensionData]
    private IDictionary<string, JToken>? _extra;

    internal void WriteExtra(JObject target)
    {
        if (_extra == null)
        {
            return;
        }

        foreach (var pair in _extra)
        {
            target[pair.Key] = pair.Value.DeepClone();
        }
    }

    protected void CopyExtraTo(ExtensibleJsonModel copy)
    {
        copy._extra = _extra?.ToDictionary(pair => pair.Key, pair => pair.Value.DeepClone());
    }
}
