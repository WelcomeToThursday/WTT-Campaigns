using WTT.Campaigns.Shared.Serialization;
using WTT.Campaigns.Shared.Story;

namespace WTT.Campaigns.Server.Web.Authoring;

public static class ConversationAuthoring
{
    public static bool HasPhase(StoryDefinition story, StoryDialog dialog)
    {
        return story.Variables.Count(v => v.Id == dialog.MainVariable) == 1
            && story.Variables.Any(v => v.Id == dialog.MainVariable && v.Scope == StoryVariableScope.Dialogue)
            && story.Dialogs.Count(d => d.MainVariable == dialog.MainVariable) == 1;
    }

    public static bool IsPhaseLine(StoryDialog dialog, StoryDialogLine line)
    {
        return line.Trigger.Type == "VariableValue" && line.Trigger.Target == dialog.MainVariable && line.Trigger.Operator == "=="
        && line.Trigger.Value == Math.Truncate(line.Trigger.Value) && line.Trigger.Value >= int.MinValue && line.Trigger.Value < int.MaxValue;
    }

    public static IEnumerable<StoryDialogLine> NextLines(StoryDialog dialog, StoryDialogLine line)
    {
        return dialog.Lines.Where(next => IsPhaseLine(dialog, next) && line.Actions.Any(a => a.Type == StoryActionType.SetVariable
            && a.Target == dialog.MainVariable && a.Value == next.Trigger.Value));
    }

    public static StoryDialogLine AddAfter(StoryDefinition story, StoryDialog dialog, StoryDialogLine source, string side)
    {
        RequirePhase(story, dialog, source);
        if (source.Actions.Any(a => a.Type == StoryActionType.QuitAction))
        {
            throw new InvalidOperationException("This line ends the conversation. Remove End conversation under Effects before adding a continuation.");
        }

        var transition = source.Actions.SingleOrDefault(a => a.Type == StoryActionType.SetVariable && a.Target == dialog.MainVariable);
        var phase = transition?.Value ?? NextPhase(story, dialog);
        if (side == "Npc" && dialog.Lines.Any(l => l.Side == "Npc" && IsPhaseLine(dialog, l) && l.Trigger.Value == phase))
        {
            throw new InvalidOperationException("This reply already leads to a trader line. Select that line in the outline to edit it, or connect to a different line.");
        }

        var line = new StoryDialogLine
        {
            Id = StoryAuthoring.NewId(), Side = side, Text = side == "Player" ? "New player reply" : "New trader line",
            Trigger = new() { Type = "VariableValue", Target = dialog.MainVariable, Operator = "==", Value = phase },
        };
        if (transition == null)
        {
            SetPhase(dialog, source, phase);
        }

        dialog.Lines.Add(line);
        // Automatic trader lines must leave their phase, even before the next reply is written.
        if (side == "Npc")
        {
            SetPhase(dialog, line, NextPhase(story, dialog));
        }

        return line;
    }

    public static void Connect(StoryDefinition story, StoryDialog dialog, StoryDialogLine source, StoryDialogLine target)
    {
        RequirePhase(story, dialog, source);
        if (!dialog.Lines.Contains(target) || !IsPhaseLine(dialog, target))
        {
            throw new InvalidOperationException("Choose a line with a single conversation phase. Edit complex transitions under Effects.");
        }

        SetPhase(dialog, source, checked((int)target.Trigger.Value));
        source.Actions.RemoveAll(a => a.Type == StoryActionType.QuitAction);
    }

    public static void End(StoryDefinition story, StoryDialog dialog, StoryDialogLine line)
    {
        RequirePhase(story, dialog, line);
        line.Actions.RemoveAll(a => a.Type == StoryActionType.SetVariable && a.Target == dialog.MainVariable);
        if (!line.Actions.Any(a => a.Type == StoryActionType.QuitAction))
        {
            line.Actions.Add(new() { Id = StoryAuthoring.NewId(), Type = StoryActionType.QuitAction });
        }
    }

    private static void RequirePhase(StoryDefinition story, StoryDialog dialog, StoryDialogLine line)
    {
        if (!dialog.Lines.Contains(line) || !HasPhase(story, dialog))
        {
            throw new InvalidOperationException("Guided connections need this conversation's own Dialogue-scope phase variable. Use Conditions and Effects for imported or shared-variable logic.");
        }

        if (!IsPhaseLine(dialog, line))
        {
            throw new InvalidOperationException("This line has a custom availability condition. Use Conditions and Effects to preserve its behavior.");
        }

        if (line.Actions.Count(a => a.Type == StoryActionType.SetVariable && a.Target == dialog.MainVariable) > 1
            || line.Actions.Any(a => a.Type is StoryActionType.SwitchDialog or StoryActionType.EmbedQuestDialog))
        {
            throw new InvalidOperationException("This line has a complex transition. Edit its actions under Effects to preserve their order and behavior.");
        }
    }

    private static int NextPhase(StoryDefinition story, StoryDialog dialog)
    {
        var used = ModelGraph.Texts(dialog).SelectMany(t => t.Ancestors).OfType<StoryCondition>().Distinct()
            .Where(c => c.Type == "VariableValue" && c.Target == dialog.MainVariable && c.Value >= int.MinValue && c.Value <= int.MaxValue)
            .Select(c => checked((int)c.Value))
            .Concat(dialog.Lines.SelectMany(l => l.Actions).Where(a => a.Type == StoryActionType.SetVariable && a.Target == dialog.MainVariable).Select(a => a.Value))
            .Concat(dialog.StartPoints.Values).Append(story.Variables.Single(v => v.Id == dialog.MainVariable).InitialValue).ToHashSet();
        var phase = 0;
        while (used.Contains(phase))
        {
            phase = checked(phase + 1);
        }

        return phase;
    }

    private static void SetPhase(StoryDialog dialog, StoryDialogLine line, int phase)
    {
        var action = line.Actions.SingleOrDefault(a => a.Type == StoryActionType.SetVariable && a.Target == dialog.MainVariable);
        if (action == null) { action = new() { Id = StoryAuthoring.NewId(), Type = StoryActionType.SetVariable, Target = dialog.MainVariable, Scope = StoryVariableScope.Dialogue }; line.Actions.Add(action); }
        action.Value = phase;
    }
}
