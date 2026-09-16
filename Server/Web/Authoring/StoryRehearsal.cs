using Newtonsoft.Json;
using WTT.Campaigns.Shared.Seasons;
using WTT.Campaigns.Shared.Story;

namespace WTT.Campaigns.Server.Web.Authoring;

// No repository, profile, inventory service, disk access or runtime routes belong in this host.
public sealed class StoryRehearsal
{
    public SeasonDefinition Season { get; }

    public StoryDefinition Definition
    {
        get { return Season.Story!; }
    }

    public StoryProgress State { get; private set; }
    public StoryFacts Facts { get; private set; }
    public List<string> Log { get; } = [];
    public string Error { get; private set; } = "";
    public RehearsalPendingException? Pending { get; private set; }
    public string Source { get; }

    private Action<StoryEngine>? _command;
    private readonly Dictionary<string, bool> _outcomes = new();
    private uint _random;
    private long _step;

    public StoryRehearsal(SeasonDefinition source, StoryFacts facts, int seed)
    {
        Source = JsonConvert.SerializeObject(source);
        Season = SeasonCompiler.Copy(source);
        Season.Story ??= new();
        Facts = SeasonCompiler.Copy(facts);
        State = new() { SeasonId = Season.Id };
        _random = (uint)seed;
        Log.Add("Rehearsal started. Quest and inventory outcomes are simulated; rewards and media are recorded only.");
    }

    public bool Stale(SeasonDefinition source)
    {
        return Source != JsonConvert.SerializeObject(source);
    }

    public IReadOnlyList<StoryDialogLine> Replies
    {
        get { return StoryRules.EligibleLines(Definition, State, Facts).Where(l => l.Side == "Player").ToArray(); }
    }

    public bool Eligible(StoryCondition condition)
    {
        return StoryRules.Evaluate(condition, Definition, State, Facts);
    }

    public void Start(string entryId)
    {
        Execute(e => e.Start(entryId, "rehearsal-" + (_step + 1)));
    }

    public void Select(string lineId)
    {
        Execute(e => e.Select(State.Conversation?.Id ?? "", lineId));
    }

    public void Refresh()
    {
        Execute(_ => { });
    }

    public void Close()
    {
        Execute(e => e.Close(State.Conversation?.Id ?? ""));
    }

    public void Configure(StoryFacts facts, IReadOnlyDictionary<string, int> variables)
    {
        Execute(_ =>
        {
            Facts = SeasonCompiler.Copy(facts);
            foreach (var variable in Definition.Variables)
            {
                if (!variables.TryGetValue(variable.Id, out var value))
                {
                    continue;
                }

                if (variable.Scope == StoryVariableScope.Profile)
                {
                    State.Variables[variable.Id] = value;
                }
                else if (variable.Scope == StoryVariableScope.Session)
                {
                    Facts.SessionVariables[variable.Id] = value;
                }
                else if (State.Conversation != null)
                {
                    State.Conversation.Variables[variable.Id] = value;
                }
            }

            Log.Add("Applied simulated facts and variable values.");
        });
    }

    public void Resume(bool success)
    {
        if (Pending == null || _command == null)
        {
            return;
        }

        if (!success)
        {
            Error = "Manual native outcome: failure. The entire step was rolled back.";
            Log.Add(Error);
            Pending = null;
            _command = null;
            _outcomes.Clear();
            return;
        }

        _outcomes[Pending.Key] = true;
        Execute(_command, true);
    }

    public void Trigger(string bindingId)
    {
        Execute(engine =>
        {
            var binding = Definition.RaidBindings.Single(b => b.Id == bindingId);
            if (!Facts.InRaid || Facts.Location != binding.Location)
            {
                throw new InvalidOperationException("Enter this event's raid location in the simulated facts first.");
            }

            if (binding.Once && State.CompletedBindings.Contains(bindingId))
            {
                throw new InvalidOperationException("This once-only binding has already completed.");
            }

            if (!Eligible(binding.Condition))
            {
                throw new InvalidOperationException("The raid event's conditions do not pass.");
            }

            State.Raid ??= new() { Id = "rehearsal-raid", Location = Facts.Location };
            if (State.Raid.Pending.Contains(bindingId))
            {
                throw new InvalidOperationException("This event is already awaiting a surviving raid result.");
            }

            if (binding.Kind != "Collectible")
            {
                var separator = binding.ObjectPath.IndexOf(":/", StringComparison.Ordinal);
                var scene = separator < 0 ? "" : binding.ObjectPath[..separator];
                if (Facts.Scene != scene)
                {
                    throw new InvalidOperationException("Enter the bound object's exact scene in the simulated facts.");
                }
            }
            if (binding.Kind == "Cinematic")
            {
                if (State.Raid.Cinematic.Length > 0)
                {
                    throw new InvalidOperationException("Finish the active cinematic first.");
                }

                State.Raid.Cinematic = binding.Id;
                Log.Add("Cinematic started; choose completion, skip or interruption.");
                return;
            }
            ActivateBinding(engine, binding);
        });
    }

    public void FinishRaid(bool survived)
    {
        Execute(engine =>
        {
            foreach (var id in State.Raid?.Pending.ToArray() ?? [])
            {
                if (survived)
                {
                    ApplyBinding(engine, Definition.RaidBindings.Single(b => b.Id == id));
                }
            }

            State.Raid = null;
            Facts.InRaid = false;
            Log.Add(survived ? "Simulated survival committed deferred raid events." : "Simulated death discarded deferred raid events.");
        });
    }

    public void FinishCinematic(string result)
    {
        Execute(engine =>
        {
            if (State.Raid?.Cinematic.Length is not > 0 || result is not ("complete" or "skip" or "interrupt"))
            {
                throw new InvalidOperationException("No matching cinematic is active.");
            }

            var binding = Definition.RaidBindings.Single(b => b.Id == State.Raid.Cinematic);
            State.Raid.Cinematic = "";
            if (result != "interrupt")
            {
                ActivateBinding(engine, binding);
            }

            Log.Add("Cinematic " + result);
        });
    }

    private void ActivateBinding(StoryEngine engine, StoryRaidBinding binding)
    {
        State.Raid!.Seen.Add(binding.Id);
        if (binding.PersistOnDeath)
        {
            ApplyBinding(engine, binding);
        }
        else
        {
            State.Raid.Pending.Add(binding.Id);
            Log.Add("Raid event queued until survival: " + StoryAuthoring.Label(binding));
        }
        if (binding.MediaId.Length > 0 && binding.Kind != "Cinematic")
        {
            Log.Add("Media played before conversation: " + binding.MediaId);
        }

        if (binding.EntryPointId.Length > 0)
        {
            engine.Start(binding.EntryPointId, "rehearsal-raid-" + (_step + 1));
        }
    }

    private void ApplyBinding(StoryEngine engine, StoryRaidBinding binding)
    {
        State.CompletedBindings.Add(binding.Id);
        if (binding.Kind == "Collectible")
        {
            State.CompletedItems.Add(binding.ItemId);
        }

        engine.Apply(binding.Actions);
        Log.Add("Simulated raid event: " + StoryAuthoring.Label(binding));
    }

    private int Random(int maximum)
    {
        if (maximum <= 0)
        {
            throw new InvalidOperationException("Random maximum must be positive.");
        }

        _random = unchecked(_random * 1664525 + 1013904223);
        return (int)(_random % (uint)maximum);
    }

    private void Execute(Action<StoryEngine> command, bool resume = false)
    {
        if (!resume)
        {
            _outcomes.Clear();
        }

        var originalState = State;
        var originalFacts = Facts;
        var originalRandom = _random;
        var logCount = Log.Count;
        State = SeasonCompiler.Copy(State);
        Facts = SeasonCompiler.Copy(Facts);
        Error = "";
        Pending = null;
        _command = command;
        try
        {
            var engine = new StoryEngine(Definition, State, Facts, Native, Random, _step + 1);
            Reconcile();
            command(engine);
            Reconcile();
            foreach (var line in engine.Lines)
            {
                Log.Add(StoryAuthoring.Label(line));
            }

            foreach (var action in engine.Presentation)
            {
                Log.Add("Presentation request recorded: " + action.Type + " " + action.Target);
            }

            foreach (var variable in Definition.Variables)
            {
                var before = StoryRules.Variable(Definition, originalState, originalFacts, variable.Id);
                var after = StoryRules.Variable(Definition, State, Facts, variable.Id);
                if (before != after)
                {
                    Log.Add($"Variable {variable.Id}: {before} → {after}");
                }
            }

            foreach (var note in State.Notes.Keys.Except(originalState.Notes.Keys))
            {
                Log.Add("Journal revealed: " + Definition.Notes.First(n => n.Id == note).Text);
            }

            _step++;
            State.Revision++;
            _command = null;
            _outcomes.Clear();
        }
        catch (Exception e) when (e is InvalidOperationException or RehearsalPendingException or ArgumentException)
        {
            State = originalState;
            Facts = originalFacts;
            _random = originalRandom;
            Log.RemoveRange(logCount, Log.Count - logCount);
            if (e is RehearsalPendingException pending)
            {
                Pending = pending;
                Log.Add("Paused: " + pending.Message);
            }
            else
            {
                Error = e.Message;
                Log.Add("Step rolled back: " + e.Message);
                _command = null;
                _outcomes.Clear();
            }
        }
    }

    private bool Manual(string key, string message)
    {
        if (_outcomes.ContainsKey(key))
        {
            Log.Add("Manual simulated success: " + message);
            return true;
        }

        throw new RehearsalPendingException(key, message);
    }

    private bool NativeCondition(NativeCondition condition)
    {
        var id = (string?)condition.Id ?? "";
        if (Facts.CompletedConditions.Contains(id))
        {
            return true;
        }

        var kind = (string?)condition.ConditionType ?? "";
        var target = condition.Target?.Values.FirstOrDefault() ?? "";
        var required = (double?)condition.Value ?? 1;
        var op = (string?)condition.CompareMethod ?? ">=";
        double? actual = kind switch
        {
            "Level" => Facts.Level,
            "TraderLoyalty" => Facts.TraderLoyalty.GetValueOrDefault(target),
            "TraderStanding" => Facts.TraderReputation.GetValueOrDefault(target),
            "Skill" => Facts.Skills.GetValueOrDefault(target),
            "HideoutArea" => Facts.HideoutAreas.GetValueOrDefault(target),
            "HandoverItem" or "CounterCreator" => Facts.ConditionCounters.GetValueOrDefault(id),
            "HasItem" when condition.Target?.Values.Count is not > 1 => Facts.Items.GetValueOrDefault(target),
            "CompleteCondition" => Facts.CompletedConditions.Contains(target) ? 1 : 0,
            "GlobalVariableValue" => StoryRules.Variable(Definition, State, Facts, target),
            "CompletableItem" => State.CompletedItems.Contains(target) ? 1 : 0,
            "LocationTrigger" => State.CompletedBindings.Contains(target) ? 1 : 0,
            _ => null,
        };
        if (kind == "Quest")
        {
            var status = Facts.QuestStatuses.GetValueOrDefault(target) ?? "Locked";
            var names = new[]
            {
                "Locked",
                "AvailableForStart",
                "Started",
                "AvailableForFinish",
                "Success",
                "Fail",
                "FailRestartable",
                "MarkedAsFailed",
                "Expired",
                "AvailableAfter",
            };
            if ((double?)condition.AvailableAfter > 0)
            {
                return Manual(id, "Quest time delay requires a native result for " + id);
            }

            return (condition.Status ?? new List<string> { "4" }).Any(v =>
                int.TryParse(v.ToString(), out var n) && n >= 0 && n < names.Length && names[n] == status
            );
        }

        return actual.HasValue
            ? StoryRules.Compare(actual.Value, op, required)
            : Manual(id, "Choose whether native objective " + kind + " succeeds: " + id);
    }

    private NativeQuest Quest(string id)
    {
        if (!Definition.Quests.Any(q => q.QuestId == id))
        {
            throw new InvalidOperationException("Only an owned story quest can be changed.");
        }

        return Season.Quests.SingleOrDefault(q => (string?)q.Id == id && (bool?)q.SeasonalEnabled != false)
            ?? throw new InvalidOperationException("External or disabled quests cannot be changed.");
    }

    private bool Stage(NativeQuest quest, string stage)
    {
        return quest.Conditions.Stage(stage).Where(c => (bool?)c.IsNecessary != false).All(NativeCondition);
    }

    private void Native(StoryAction action)
    {
        if (action.Type == StoryActionType.TraderStanding)
        {
            if (!SeasonValidator.IsId(action.Target) || !double.IsFinite(action.StandingChange))
                throw new InvalidOperationException("Choose a valid trader and finite reputation change.");
            Facts.TraderReputation[action.Target] = Math.Round((Facts.TraderReputation.GetValueOrDefault(action.Target) + action.StandingChange) * 100, 2) / 100;
            return;
        }
        var id = action.QuestId.Length > 0 ? action.QuestId : State.Conversation?.SelectedQuestId ?? "";
        var quest = Quest(id);
        var status = Facts.QuestStatuses.GetValueOrDefault(id) ?? "Locked";
        switch (action.Type)
        {
            case StoryActionType.FailQuest:
                if (status is "Fail" or "MarkedAsFailed")
                    return;
                if (status is not ("Started" or "AvailableForFinish"))
                    throw new InvalidOperationException("Only an active story quest can be failed.");
                Facts.QuestStatuses[id] = "Fail";
                Log.Add("Native failure rewards recorded, not granted: " + JsonConvert.SerializeObject(quest.Rewards.GetValueOrDefault("Fail") ?? new()));
                break;
            case StoryActionType.AcceptQuest:
                if (status is "Started" or "AvailableForFinish" or "Success")
                {
                    return;
                }

                if (status is "Fail" or "MarkedAsFailed" || !Stage(quest, "AvailableForStart"))
                {
                    throw new InvalidOperationException("Start requirements do not pass for " + NativeQuestAuthoring.QuestName(quest));
                }

                Facts.QuestStatuses[id] = "Started";
                break;
            case StoryActionType.FinishQuest:
            case StoryActionType.PlayerReward:
                if (status == "Success")
                {
                    return;
                }

                if (status is not ("Started" or "AvailableForFinish") || !Stage(quest, "AvailableForFinish"))
                {
                    throw new InvalidOperationException("Complete required objectives before finishing this quest.");
                }

                Facts.QuestStatuses[id] = "Success";
                Log.Add(
                    "Native rewards recorded, not granted: "
                        + JsonConvert.SerializeObject(quest.Rewards.GetValueOrDefault("Success") ?? new())
                );
                break;
            case StoryActionType.HandoverItem:
                if (Facts.InRaid || status is not ("Started" or "AvailableForFinish"))
                {
                    throw new InvalidOperationException("Handovers require an active quest outside a raid.");
                }

                var condition =
                    quest.Conditions.AvailableForFinish.SingleOrDefault(c =>
                        (string?)c.Id == action.ConditionId && (string?)c.ConditionType == "HandoverItem"
                    ) ?? throw new InvalidOperationException("Choose a handover objective belonging to this quest.");
                var targets = condition.Target?.Values;
                var target = targets?.FirstOrDefault() ?? "";
                var remaining = Math.Max(
                    0,
                    ((double?)condition.Value ?? 1) - Facts.ConditionCounters.GetValueOrDefault(action.ConditionId)
                );
                if (remaining == 0)
                {
                    return;
                }

                var complex =
                    targets?.Count != 1
                    || (bool?)condition.OnlyFoundInRaid == true
                    || (double?)condition.MinDurability > 0
                    || ((double?)condition.MaxDurability ?? 100) < 100;
                if (complex)
                {
                    Manual(
                        action.Id.Length > 0 ? action.Id : action.ConditionId,
                        "Filtered handover requires native item eligibility. Simulate completing this objective?"
                    );
                    Facts.ConditionCounters[action.ConditionId] = (double?)condition.Value ?? 1;
                    Facts.CompletedConditions.Add(action.ConditionId);
                }
                else
                {
                    var count = Math.Min(
                        remaining,
                        Math.Min(Facts.Items.GetValueOrDefault(target), Facts.HandoverItems.GetValueOrDefault(action.ConditionId))
                    );
                    if (count <= 0)
                    {
                        throw new InvalidOperationException(
                            "No simulated eligible items. Set inventory count and eligible handover count for this objective."
                        );
                    }

                    Facts.Items[target] -= count;
                    Facts.HandoverItems[action.ConditionId] -= count;
                    Facts.ConditionCounters[action.ConditionId] = Facts.ConditionCounters.GetValueOrDefault(action.ConditionId) + count;
                }

                break;
            default:
                throw new InvalidOperationException("Unsupported native action: " + action.Type);
        }

        Log.Add("Simulated " + action.Type + ": " + NativeQuestAuthoring.QuestName(quest));
    }

    private void Reconcile()
    {
        for (var pass = 0; pass <= Definition.Quests.Count + 1; pass++)
        {
            var changed = false;
            foreach (var membership in Definition.Quests)
            {
                var quest = Season.Quests.FirstOrDefault(q => (string?)q.Id == membership.QuestId && (bool?)q.SeasonalEnabled != false);
                if (quest == null)
                {
                    continue;
                }

                var status = Facts.QuestStatuses.GetValueOrDefault(membership.QuestId) ?? "Locked";
                if (
                    membership.AutoStart
                    && status is "Locked" or "AvailableForStart"
                    && Eligible(membership.Visibility)
                    && Stage(quest, "AvailableForStart")
                )
                {
                    Native(new() { Type = StoryActionType.AcceptQuest, QuestId = membership.QuestId });
                    changed = true;
                }

                status = Facts.QuestStatuses.GetValueOrDefault(membership.QuestId) ?? "Locked";
                if (status is "Started" or "AvailableForFinish")
                {
                    foreach (var condition in quest.Conditions.AvailableForFinish)
                    {
                        var conditionId = (string?)condition.Id ?? "";
                        if (!Facts.CompletedConditions.Contains(conditionId) && NativeCondition(condition))
                        {
                            changed |= Facts.CompletedConditions.Add(conditionId);
                        }
                    }

                    if (quest.Conditions.Fail.Any(NativeCondition))
                    {
                        Facts.QuestStatuses[membership.QuestId] = "Fail";
                        changed = true;
                    }
                    else if (membership.AutoComplete && Stage(quest, "AvailableForFinish"))
                    {
                        Native(new() { Type = StoryActionType.FinishQuest, QuestId = membership.QuestId });
                        changed = true;
                    }
                }

                if (
                    membership.StatusNotes.TryGetValue(Facts.QuestStatuses.GetValueOrDefault(membership.QuestId) ?? "Locked", out var notes)
                )
                {
                    foreach (var note in notes)
                    {
                        State.Notes.TryAdd(note, _step + 1);
                    }
                }
            }

            foreach (
                var note in Definition.Notes.Where(n => n.ConditionIds.Count > 0 && n.ConditionIds.All(Facts.CompletedConditions.Contains))
            )
            {
                State.Notes.TryAdd(note.Id, _step + 1);
            }

            if (!changed)
            {
                return;
            }
        }

        throw new InvalidOperationException("Automatic quest progression did not stabilize.");
    }
}
