using Newtonsoft.Json.Converters;

namespace WTT.Campaigns.Shared.Story;

public sealed class StoryEnumConverter : StringEnumConverter
{
    public StoryEnumConverter()
    {
        AllowIntegerValues = false;
    }

    public override bool CanConvert(Type objectType)
    {
        // EFT can discover this converter globally; story wire rules must not apply to game or mod enums.
        var type = Nullable.GetUnderlyingType(objectType) ?? objectType;
        return type == typeof(StoryActionType) || type == typeof(StoryVariableScope);
    }
}
