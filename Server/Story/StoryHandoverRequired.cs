using WTT.Campaigns.Shared.Story;

namespace WTT.Campaigns.Server.Story;

internal sealed class StoryHandoverRequired(StoryHandover handover) : Exception
{
    internal StoryHandover Handover { get; } = handover;
}
