namespace WTT.Campaigns.Shared.Story;

public static class StoryContent
{
    public static IEnumerable<string> OwnedIds(StoryDefinition? story)
    {
        if (story == null)
            yield break;
        foreach (var chapter in story.Chapters)
            yield return chapter.Id;
        foreach (var note in story.Notes)
            yield return note.Id;
        foreach (var note in story.Notes)
        foreach (var link in note.Links)
            yield return link.Id;
        foreach (var dialog in story.Dialogs)
            yield return dialog.Id;
        foreach (var dialog in story.Dialogs)
        foreach (var line in dialog.Lines)
            yield return line.Id;
        foreach (var dialog in story.Dialogs)
        foreach (var line in dialog.Lines)
        foreach (var action in line.Actions)
            yield return action.Id;
        foreach (var variable in story.Variables)
            yield return variable.Id;
        foreach (var entry in story.EntryPoints)
            yield return entry.Id;
        foreach (var binding in story.RaidBindings)
            yield return binding.Id;
        foreach (var binding in story.RaidBindings)
        foreach (var action in binding.Actions)
            yield return action.Id;
        foreach (var media in story.Media)
            yield return media.Id;
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
        foreach (var line in story.Dialogs.AsValueEnumerable().SelectMany(d => d.Lines))
        {
            texts[line.Id + " text"] = line.Text;
            if (line.Confirmation.Length > 0)
            {
                texts[line.Id + " confirmation"] = line.Confirmation;
            }
        }
    }
}
