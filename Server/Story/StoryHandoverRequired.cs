using SeasonalPerks.Shared.Story;

namespace SeasonalPerks.Server.Story;

internal sealed class StoryHandoverRequired(StoryHandover handover) : Exception
{
    internal StoryHandover Handover { get; } = handover;
}
