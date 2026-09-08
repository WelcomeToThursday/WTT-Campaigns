using System;

namespace SeasonalPerks.UI.Models;

public sealed class StoryChapterView
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Status { get; set; } = "";
    public string Image { get; set; } = "";
    public string Icon { get; set; } = "";
    public bool Unread { get; set; }
    public StoryNoteView[] Notes { get; set; } = Array.Empty<StoryNoteView>();
    public StoryObjectiveView[] Objectives { get; set; } = Array.Empty<StoryObjectiveView>();
    public StoryLinkView[] Links { get; set; } = Array.Empty<StoryLinkView>();
}
