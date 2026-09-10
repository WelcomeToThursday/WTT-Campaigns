using WTT.Campaigns.Shared.Story;

namespace WTT.Campaigns.Tests;

internal static class StoryEngineChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var definition = new StoryDefinition
        {
            Variables = [new() { Id = "phase", Scope = StoryVariableScope.Profile }],
            Notes = [new() { Id = "note" }],
            Dialogs =
            [
                new()
                {
                    Id = "dialog",
                    TraderId = "trader",
                    MainVariable = "phase",
                    Lines =
                    [
                        new()
                        {
                            Id = "greeting",
                            Side = "Npc",
                            Trigger = Phase(0),
                            Actions = [Set(1)],
                        },
                        new()
                        {
                            Id = "accept",
                            Side = "Player",
                            Trigger = Phase(1),
                            Actions = [Set(2), new() { Type = StoryActionType.AcceptQuest, QuestId = "quest" }],
                        },
                        new()
                        {
                            Id = "accepted",
                            Side = "Npc",
                            Trigger = Phase(2),
                            Actions = [Set(3), new() { Type = StoryActionType.DiaryNote, Target = "note" }],
                        },
                        new()
                        {
                            Id = "exit",
                            Side = "Player",
                            Trigger = Phase(3),
                            Actions = [new() { Type = StoryActionType.QuitAction }],
                        },
                    ],
                },
            ],
            EntryPoints =
            [
                new()
                {
                    Id = "entry",
                    DialogId = "dialog",
                    TraderId = "trader",
                },
            ],
        };
        var state = new StoryProgress();
        var facts = new StoryFacts();
        var accepts = 0;
        var engine = new StoryEngine(definition, state, facts, _ => accepts++, _ => 0, 123);
        engine.Start("entry", "conversation");
        check(
            state.Conversation?.CurrentLineId == "greeting" && state.Variables["phase"] == 1,
            "Automatic greeting advances to a player choice"
        );
        check(StoryRules.EligibleLines(definition, state, facts).Single().Id == "accept", "Only the current reply is selectable");
        engine.Select("conversation", "accept");
        check(
            accepts == 1 && state.Notes["note"] == 123 && state.Conversation!.CurrentLineId == "accepted",
            "Choice runs native action once and unlocks journal response"
        );
        Reject(() => engine.Select("conversation", "accept"), "A stale reply cannot run its native action twice");
        Reject(() => engine.Select("another", "exit"), "Conversation identity prevents cross-conversation actions");
        engine.Select("conversation", "exit");
        check(state.Conversation!.Closed, "Exit closes conversation");
        engine.Start("entry", "next");
        check(state.Variables["phase"] == 3 && accepts == 1, "Reentering preserves durable dialogue progress and rewards");
        engine.Close("next");
        facts.InRaid = true;
        Reject(() => engine.Start("entry", "raid"), "Lobby trader entry cannot open in a raid");
        facts.InRaid = false;
        definition
            .Dialogs[0]
            .Lines.Add(
                new()
                {
                    Id = "loop",
                    Side = "Npc",
                    Trigger = Phase(3),
                }
            );
        Reject(() => engine.Start("entry", "loop"), "Automatic dialogue loops abort the staged operation");

        void Reject(Action action, string message)
        {
            var rejected = false;
            try
            {
                action();
            }
            catch (InvalidOperationException)
            {
                rejected = true;
            }
            check(rejected, message);
        }
    }

    private static StoryCondition Phase(int value)
    {
        return new()
        {
            Type = "VariableValue",
            Target = "phase",
            Operator = "==",
            Value = value,
        };
    }

    private static StoryAction Set(int value)
    {
        return new()
        {
            Type = StoryActionType.SetVariable,
            Target = "phase",
            Value = value,
            Scope = StoryVariableScope.Profile,
        };
    }
}
