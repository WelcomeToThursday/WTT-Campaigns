using WTT.Campaigns.Shared.Story;

namespace WTT.Campaigns.Client.Story;

internal sealed class PendingStoryOperation
{
    public string Operation { get; set; } = "";
    public StoryRequest Request { get; set; } = new();
}
