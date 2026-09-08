using SeasonalPerks.Shared.Story;

namespace SeasonalPerks.Client.Story;

internal sealed class PendingStoryOperation
{
    public string Operation { get; set; } = "";
    public StoryRequest Request { get; set; } = new();
}
