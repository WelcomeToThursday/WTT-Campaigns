using Newtonsoft.Json.Converters;

namespace WTT.Campaigns.Shared.Story;

public sealed class StoryEnumConverter : StringEnumConverter
{
    public StoryEnumConverter()
    {
        AllowIntegerValues = false;
    }
}
