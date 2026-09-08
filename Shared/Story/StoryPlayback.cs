namespace SeasonalPerks.Shared.Story;

public sealed class StoryPlayback
{
    public List<StorySequence> Animations { get; set; } = new();
    public List<StorySequence> SecondaryAnimations { get; set; } = new();
    public List<StorySequence> LipSyncs { get; set; } = new();
    public List<StorySequence> Subtitles { get; set; } = new();
    public string Image { get; set; } = "";
    public string Music { get; set; } = "";
    public string Sound { get; set; } = "";
}
