namespace WTT.Campaigns.Shared.Story;

public sealed class StoryDefinition
{
    public int FormatVersion { get; set; } = 1;
    public List<StoryChapter> Chapters { get; set; } = new();
    public List<StoryQuest> Quests { get; set; } = new();
    public List<StoryNote> Notes { get; set; } = new();
    public List<StoryDialog> Dialogs { get; set; } = new();
    public List<StoryVariable> Variables { get; set; } = new();
    public List<StoryEntryPoint> EntryPoints { get; set; } = new();
    public List<StoryRaidBinding> RaidBindings { get; set; } = new();
    public List<StoryMedia> Media { get; set; } = new();
}
