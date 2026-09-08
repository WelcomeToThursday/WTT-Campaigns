namespace SeasonalPerks.Shared.Story;

public static class StoryContent
{
    public static IEnumerable<string> OwnedIds(StoryDefinition? story)
    {
        if (story == null)
        {
            return Enumerable.Empty<string>();
        }
        return story
            .Chapters.Select(c => c.Id)
            .Concat(story.Notes.Select(n => n.Id))
            .Concat(story.Notes.SelectMany(n => n.Links.Select(l => l.Id)))
            .Concat(story.Dialogs.Select(d => d.Id))
            .Concat(story.Dialogs.SelectMany(d => d.Lines.Select(l => l.Id)))
            .Concat(story.Dialogs.SelectMany(d => d.Lines.SelectMany(l => l.Actions.Select(a => a.Id))))
            .Concat(story.Variables.Select(v => v.Id))
            .Concat(story.EntryPoints.Select(e => e.Id))
            .Concat(story.RaidBindings.Select(b => b.Id))
            .Concat(story.RaidBindings.SelectMany(b => b.Actions.Select(a => a.Id)))
            .Concat(story.Media.Select(m => m.Id));
    }

    public static void AddTexts(StoryDefinition? story, Dictionary<string, string> texts)
    {
        if (story == null)
        {
            return;
        }
        foreach (var chapter in story.Chapters)
        {
            texts[chapter.Id + " name"] = chapter.Name;
        }
        foreach (var note in story.Notes)
        {
            texts[note.Id + " text"] = note.Text;
        }
        foreach (var line in story.Dialogs.SelectMany(d => d.Lines))
        {
            texts[line.Id + " text"] = line.Text;
            if (line.Confirmation.Length > 0)
            {
                texts[line.Id + " confirmation"] = line.Confirmation;
            }
        }
    }
}
