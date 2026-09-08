using Newtonsoft.Json;

namespace SeasonalPerks.Shared.Story;

[JsonConverter(typeof(StoryEnumConverter))]
public enum StoryVariableScope
{
    Profile,
    Session,
    Dialogue,
}
