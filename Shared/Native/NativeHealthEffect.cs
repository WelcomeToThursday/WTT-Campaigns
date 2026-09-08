using Newtonsoft.Json;
using SeasonalPerks.Shared.Serialization;

namespace SeasonalPerks.Shared.Native;

public sealed class NativeHealthEffect : NativeModel
{
    [JsonProperty("bodyParts")]
    public List<string>? BodyParts { get; set; }

    [JsonProperty("effects")]
    public List<string>? Effects { get; set; }
}
