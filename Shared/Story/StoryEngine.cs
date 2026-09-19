namespace WTT.Campaigns.Shared.Story;

// Runs on the authority. The host supplies native quest/service effects and refreshed facts.
public sealed class StoryEngine(
    StoryDefinition definition,
    StoryProgress state,
    StoryFacts facts,
    Action<StoryAction> nativeAction,
    Func<int, int> random,
    long timestamp
)
{
    public List<StoryAction> Presentation { get; } = new();
    public List<StoryDialogLine> Lines { get; } = new();
    public string EventMediaId { get; set; } = "";
    public string CinematicBindingId { get; set; } = "";

    public void Start(string entryId, string conversationId)
    {
        var entry =
            definition.EntryPoints.AsValueEnumerable().SingleOrDefault(e => e.Id == entryId)
            ?? throw new InvalidOperationException("Unknown conversation entry point.");
        if (entry.Kind == "InLobby" && facts.InRaid || entry.Kind != "InLobby" && !facts.InRaid)
        {
            throw new InvalidOperationException("This conversation is not available here.");
        }
        facts.TraderId = entry.TraderId;
        if (!StoryProjection.EntryAvailable(entry, definition, state, facts))
        {
            throw new InvalidOperationException("This conversation is not available yet.");
        }
        state.Conversation = new StoryConversation
        {
            Id = conversationId,
            EntryPointId = entry.Id,
            DialogId = entry.DialogId,
            TraderId = entry.TraderId,
        };
        EnterDialog(entry.DialogId, entry.StartPoint);
        Advance();
    }

    public void Select(string conversationId, string lineId)
    {
        var conversation = RequireConversation(conversationId);
        facts.TraderId = conversation.TraderId;
        var line =
            StoryRules
                .EligibleLines(definition, state, facts)
                .AsValueEnumerable()
                .SingleOrDefault(l => l.Id == lineId && l.Side == "Player")
            ?? throw new InvalidOperationException("This reply is no longer available.");
        Execute(line);
        Advance();
    }

    public void Close(string conversationId)
    {
        RequireConversation(conversationId).Closed = true;
    }

    public void Apply(IEnumerable<StoryAction> actions)
    {
        foreach (var action in actions)
        {
            switch (action.Type)
            {
                case StoryActionType.UnlockMission:
                    state.UnlockedMissionLinks.Add(action.Target);
                    break;
                case StoryActionType.SetVariable:
                    SetVariable(action.Target, action.Value, action.Scope);
                    break;
                case StoryActionType.DiaryNote:
                    if (!definition.Notes.AsValueEnumerable().Any(n => n.Id == action.Target))
                    {
                        throw new InvalidOperationException("Unknown journal note.");
                    }
                    state.Notes.TryAdd(action.Target, timestamp);
                    break;
                case StoryActionType.CompleteItem:
                    state.CompletedItems.Add(action.Target);
                    break;
                case StoryActionType.SwitchDialog:
                case StoryActionType.EmbedQuestDialog:
                    if (state.Conversation == null)
                    {
                        throw new InvalidOperationException("A conversation is required.");
                    }
                    if (action.Type == StoryActionType.EmbedQuestDialog)
                    {
                        state.Conversation.DialogStack.Add(state.Conversation.DialogId);
                    }
                    EnterDialog(action.Target, "");
                    break;
                case StoryActionType.SelectQuest:
                    RequireConversation(state.Conversation?.Id ?? "").SelectedQuestId = action.QuestId;
                    break;
                case StoryActionType.SelectSubService:
                    RequireConversation(state.Conversation?.Id ?? "").SelectedServiceId = action.Target;
                    Presentation.Add(action);
                    break;
                case StoryActionType.QuitAction:
                    var conversation = RequireConversation(state.Conversation?.Id ?? "");
                    if (conversation.DialogStack.Count > 0)
                    {
                        var last = conversation.DialogStack.Count - 1;
                        conversation.DialogId = conversation.DialogStack[last];
                        conversation.DialogStack.RemoveAt(last);
                        PrepareRandom();
                    }
                    else
                    {
                        conversation.Closed = true;
                    }
                    break;
                case StoryActionType.TradingScreenAction:
                case StoryActionType.QuestsScreenAction:
                case StoryActionType.StartCinematic:
                    Presentation.Add(action);
                    break;
                default:
                    nativeAction(action);
                    break;
            }
        }
    }

    private StoryConversation RequireConversation(string id)
    {
        return state.Conversation is { Closed: false } current && current.Id == id
            ? current
            : throw new InvalidOperationException("The conversation ended or belongs to another character.");
    }

    private void SetVariable(string id, int value, StoryVariableScope scope)
    {
        var variable = definition.Variables.AsValueEnumerable().Single(v => v.Id == id);
        if (variable.Scope != scope)
        {
            throw new InvalidOperationException("Incorrect variable scope.");
        }
        var values = scope switch
        {
            StoryVariableScope.Profile => state.Variables,
            StoryVariableScope.Session => facts.SessionVariables,
            StoryVariableScope.Dialogue => RequireConversation(state.Conversation?.Id ?? "").Variables,
            _ => throw new InvalidOperationException("Unknown variable scope."),
        };
        values[id] = value;
    }

    private void EnterDialog(string id, string startPoint)
    {
        var dialog = definition.Dialogs.AsValueEnumerable().Single(d => d.Id == id);
        var conversation = RequireConversation(state.Conversation?.Id ?? "");
        if (dialog.TraderId != conversation.TraderId)
        {
            throw new InvalidOperationException("The dialog belongs to another trader.");
        }
        conversation.DialogId = id;
        if (dialog.MainVariable.Length > 0)
        {
            var variable = definition.Variables.AsValueEnumerable().Single(v => v.Id == dialog.MainVariable);
            if (startPoint.Length > 0 || variable.Scope == StoryVariableScope.Dialogue)
            {
                var initial = startPoint.Length == 0 ? variable.InitialValue : dialog.StartPoints[startPoint];
                SetVariable(variable.Id, initial, variable.Scope);
            }
        }
        PrepareRandom();
    }

    private void PrepareRandom()
    {
        var conversation = RequireConversation(state.Conversation?.Id ?? "");
        conversation.RandomValues.Clear();
        foreach (
            var group in definition
                .Dialogs.AsValueEnumerable()
                .Single(d => d.Id == conversation.DialogId)
                .Lines.AsValueEnumerable()
                .Where(l => l.Random != null)
                .Select(l => l.Random!)
                .GroupBy(r => r.VariableId + ":" + r.Group)
        )
        {
            conversation.RandomValues[group.Key] = random(group.AsValueEnumerable().First().Maximum);
        }
    }

    private void Execute(StoryDialogLine line)
    {
        Lines.Add(line);
        var conversation = RequireConversation(state.Conversation?.Id ?? "");
        conversation.CurrentLineId = line.Id;
        conversation.History.Add(line.Id);
        if (conversation.History.Count > 1000)
        {
            conversation.History.RemoveAt(0);
        }
        Apply(line.Actions);
    }

    private void Advance()
    {
        var visited = new HashSet<string>();
        while (state.Conversation is { Closed: false })
        {
            var candidates = StoryRules.EligibleLines(definition, state, facts).AsValueEnumerable().Where(l => l.Side == "Npc").ToArray();
            if (candidates.Length == 0)
            {
                return;
            }
            if (candidates.Length != 1 || !visited.Add(candidates[0].Id) || visited.Count > 64)
            {
                throw new InvalidOperationException("The dialog contains an ambiguous or looping automatic reply.");
            }
            Execute(candidates[0]);
        }
    }
}
