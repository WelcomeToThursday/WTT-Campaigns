using Newtonsoft.Json;

namespace WTT.Campaigns.Shared.Story;

[JsonConverter(typeof(StoryEnumConverter))]
public enum StoryVariableScope
{
    Profile,
    Session,
    Dialogue,
}
