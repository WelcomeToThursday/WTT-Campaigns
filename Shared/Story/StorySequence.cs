namespace WTT.Campaigns.Shared.Story;

public sealed class StorySequence
{
    public string Key { get; set; } = "";
    public float Start { get; set; }
    public float End { get; set; }
    public float Speed { get; set; } = 1;
    public float Volume { get; set; } = 1;
}
