using Newtonsoft.Json;
using WTT.Campaigns.Shared.Serialization;

namespace WTT.Campaigns.Shared.Native;

public sealed class NativeHealthEffect : NativeModel
{
    [JsonProperty("bodyParts")]
    public List<string>? BodyParts { get; set; }

    [JsonProperty("effects")]
    public List<string>? Effects { get; set; }
}
