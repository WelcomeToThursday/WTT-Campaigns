using Newtonsoft.Json.Converters;

namespace SeasonalPerks.Shared.Story;

public sealed class StoryEnumConverter : StringEnumConverter
{
    public StoryEnumConverter()
    {
        AllowIntegerValues = false;
    }
}
