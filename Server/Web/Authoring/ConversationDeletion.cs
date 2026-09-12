using WTT.Campaigns.Shared.Seasons;
using WTT.Campaigns.Shared.Story;

namespace WTT.Campaigns.Server.Web.Authoring;

public sealed record ConversationDeletionPlan(int EntryPoints, bool RemovePhase, IReadOnlyList<ChapterDeletionUse> Uses);

public static class ConversationDeletion
{
    public static ConversationDeletionPlan Check(SeasonDefinition season, string dialogId)
    {
        var dialog = season.Story?.Dialogs.FirstOrDefault(d => d.Id == dialogId)
            ?? throw new ArgumentException("Select an existing conversation.");
        var entries = season.Story!.EntryPoints.Where(e => e.DialogId == dialogId).ToArray();
        var removed = new List<object> { dialog };
        removed.AddRange(entries);
        var candidate = SeasonCompiler.Copy(season);
        RemoveConversation(candidate, dialogId);
        // A phase shared with any surviving record must be kept, even if it was created by a template.
        var phase = candidate.Story!.Variables.FirstOrDefault(v => v.Id == dialog.MainVariable);
        var removePhase = phase is { Scope: StoryVariableScope.Dialogue } && StoryAuthoring.Uses(candidate, phase).Count == 0;
        var uses = StoryAuthoring.Uses(candidate, removed).Select(path => ChapterDeletion.Describe(candidate, season, path)).ToArray();
        return new(entries.Length, removePhase, uses);
    }

    public static ConversationDeletionPlan Delete(SeasonDefinition season, string dialogId)
    {
        // Recheck the current draft at confirmation time; a newly added reference must still block deletion.
        var plan = Check(season, dialogId);
        if (plan.Uses.Count > 0)
        {
            return plan;
        }
        var phaseId = season.Story!.Dialogs.Single(d => d.Id == dialogId).MainVariable;
        RemoveConversation(season, dialogId);
        if (plan.RemovePhase)
        {
            season.Story.Variables.RemoveAll(v => v.Id == phaseId);
        }
        return plan;
    }

    private static void RemoveConversation(SeasonDefinition season, string dialogId)
    {
        season.Story!.EntryPoints.RemoveAll(e => e.DialogId == dialogId);
        season.Story.Dialogs.RemoveAll(d => d.Id == dialogId);
    }
}
