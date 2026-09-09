namespace SeasonalPerks.Shared.Story;

// The protocol uses CLR names understood by both serializers. Native item JSON stays
// inside Data so SPT's request serializer cannot discard _id/_tpl or resource fields.
public sealed class StoryObservedItem
{
    public string Id { get; set; } = "";
    public string Template { get; set; } = "";
    public int StackCount { get; set; } = 1;
    public string Data { get; set; } = "";
}
