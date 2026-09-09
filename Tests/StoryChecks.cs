using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SeasonalPerks.Server.Seasons;
using SeasonalPerks.Shared.Seasons;
using SeasonalPerks.Shared.Story;

namespace SeasonalPerks.Tests;

internal static class StoryChecks
{
    private const string Chapter = "100000000000000000000001";
    private const string Quest = "100000000000000000000002";
    private const string Variable = "100000000000000000000003";
    private const string Dialog = "100000000000000000000004";
    private const string Note = "100000000000000000000005";
    private const string Line = "100000000000000000000006";
    private const string Action = "100000000000000000000007";
    private const string Entry = "100000000000000000000008";
    private const string Trader = "54cb50c76803fa8b248b4571";

    internal static void Run(Action<bool, string> check)
    {
        var kills = new NativeCondition { ConditionType = "Kills" };
        check(!StoryQuestCompatibility.Supports(kills), "Native kill filters cannot be standalone story objectives");
        var counter = new NativeCondition
        {
            ConditionType = "CounterCreator",
            Counter = new() { Conditions = new() { kills } },
        };
        check(StoryQuestCompatibility.Supports(counter.Counter!.Conditions[0], true), "Native kill filters are supported within counters");
        var season = new SeasonDefinition { Id = "100000000000000000000010", BattlePassId = "100000000000000000000011" };
        var legacy = SeasonCompiler.GameplayIdentity(season);
        check(!JObject.FromObject(season).ContainsKey("Story"), "Story extension is absent in existing pack serialization");
        var copy = JsonConvert.DeserializeObject<SeasonDefinition>(JsonConvert.SerializeObject(season))!;
        check(legacy == SeasonCompiler.GameplayIdentity(copy), "Absent story preserves existing gameplay identity");
        season.Dependencies.Add("quest:" + Quest);
        season.Story = Definition();
        var report = new SeasonValidationResult();
        StoryValidator.Validate(season, report);
        check(report.CanPublish, "Synthetic story cross-references validate");
        var unsupported = SeasonCompiler.Copy(season);
        unsupported.Story!.Dialogs[0].Lines[0].Actions[0].Type = StoryActionType.PurchaseService;
        var unsupportedReport = new SeasonValidationResult();
        StoryValidator.Validate(unsupported, unsupportedReport);
        check(!unsupportedReport.CanPublish, "Paid services without beta adapters are rejected before publication");
        unsupported = SeasonCompiler.Copy(season);
        unsupported.Story!.Media.Add(
            new StoryMedia
            {
                Id = "100000000000000000000099",
                Bundle = "examples/test.bundle",
                Asset = "image",
            }
        );
        unsupportedReport = new SeasonValidationResult();
        StoryValidator.Validate(unsupported, unsupportedReport);
        check(!unsupportedReport.CanPublish, "Story media without a checksum cannot publish");
        unsupported.Story.Media[0].Sha256 = new string('a', 64);
        unsupportedReport = new SeasonValidationResult();
        StoryValidator.Validate(unsupported, unsupportedReport);
        check(unsupportedReport.CanPublish, "Named local media with a SHA-256 checksum validates");
        unsupported.Story.Media[0].Kind = "TraderScene";
        unsupported.Story.Media[0].TraderId = Trader;
        unsupportedReport = new SeasonValidationResult();
        StoryValidator.Validate(unsupported, unsupportedReport);
        check(unsupportedReport.CanPublish, "One assigned custom trader room validates");
        var secondRoom = JsonConvert.DeserializeObject<StoryMedia>(JsonConvert.SerializeObject(unsupported.Story.Media[0]))!;
        secondRoom.Id = "100000000000000000000098";
        unsupported.Story.Media.Add(secondRoom);
        unsupportedReport = new SeasonValidationResult();
        StoryValidator.Validate(unsupported, unsupportedReport);
        check(!unsupportedReport.CanPublish, "Two assigned rooms for one trader cannot publish");
        unsupported.Story.Media.Remove(secondRoom);
        unsupported.Story.Media[0].TraderId = "";
        unsupportedReport = new SeasonValidationResult();
        StoryValidator.Validate(unsupported, unsupportedReport);
        check(
            unsupportedReport.CanPublish && unsupportedReport.Issues.Any(i => i.Severity == "warning"),
            "Legacy unassigned trader rooms remain publishable with a warning"
        );
        var initialHash = SeasonRepository.GameplayHash(season);
        copy = SeasonCompiler.Copy(season);
        copy.Story!.Chapters[0].Name = "A translated chapter";
        copy.Story.Notes[0].Text = "A translated journal entry";
        copy.Story.Dialogs[0].Lines[0].Text = "A translated line";
        check(initialHash == SeasonRepository.GameplayHash(copy), "Story text edits preserve used-season gameplay hash");
        copy.Story.Variables[0].InitialValue = 99;
        check(initialHash != SeasonRepository.GameplayHash(copy), "Story variable changes alter gameplay hash");
        copy = SeasonRepository.Duplicate(season);
        check(
            copy.Story!.Chapters[0].Id != Chapter && copy.Story.Quests[0].ChapterId == copy.Story.Chapters[0].Id,
            "Duplication rewrites story chapter references"
        );
        check(
            copy.Story.Dialogs[0].MainVariable == copy.Story.Variables[0].Id
                && copy.Story.Dialogs[0].Lines[0].Actions[0].Target == copy.Story.Variables[0].Id,
            "Duplication rewrites declared variables and action references"
        );
        check(
            copy.Story.Quests[0].QuestId == Quest && copy.Story.Dialogs[0].TraderId == Trader,
            "Duplication preserves external quest and trader identities"
        );
        check(copy.Story.EntryPoints[0].DialogId == copy.Story.Dialogs[0].Id, "Duplication rewrites dialogue entries");
        var state = new StoryProgress { SeasonId = season.Id };
        var facts = new StoryFacts { Level = 10, TraderId = Trader };
        var definition = season.Story;
        bool Evaluate(StoryCondition condition)
        {
            return StoryRules.Evaluate(condition, definition, state, facts);
        }
        check(Evaluate(new()), "Empty conjunction is an unconditional entry");
        check(!Evaluate(new() { Type = "Any" }), "Empty disjunction cannot unlock a branch");
        check(Evaluate(new() { Type = "Level", Value = 10 }), "Exact level boundary");
        check(!Evaluate(new() { Type = "Level", Value = 11 }), "Below level boundary");
        check(
            Evaluate(
                new()
                {
                    Type = "VariableValue",
                    Target = Variable,
                    Operator = "==",
                    Value = 0,
                }
            ),
            "Initial declared variable value"
        );
        state.Variables[Variable] = 2;
        check(
            Evaluate(
                new()
                {
                    Type = "VariableValue",
                    Target = Variable,
                    Operator = "==",
                    Value = 2,
                }
            ),
            "Persistent variable drives branches"
        );
        facts.TraderReputation[Trader] = .69;
        check(
            !Evaluate(
                new()
                {
                    Type = "TraderReputation",
                    Target = Trader,
                    Value = .7,
                }
            ),
            "Fractional reputation below boundary"
        );
        facts.TraderReputation[Trader] = .7;
        check(
            Evaluate(
                new()
                {
                    Type = "TraderReputation",
                    Target = Trader,
                    Value = .7,
                }
            ),
            "Fractional reputation exact boundary"
        );
        check(!StoryRules.Compare(double.NaN, "!=", 0), "Nonfinite conditions fail closed");
        check(
            Evaluate(
                new()
                {
                    Type = "QuestStatus",
                    Target = Quest,
                    Status = ["Locked"],
                }
            ),
            "Unstarted quests have locked status"
        );
        facts.QuestStatuses[Quest] = "Started";
        check(!StoryRules.ChapterComplete(definition.Chapters[0], definition, facts), "Started quest does not complete a chapter");
        facts.QuestStatuses[Quest] = "Success";
        check(StoryRules.ChapterComplete(definition.Chapters[0], definition, facts), "Required quest success completes chapter");
        check(!StoryRules.ChapterComplete(new() { Id = "missing" }, definition, facts), "Empty chapter is not automatically completed");
        state.Conversation = new()
        {
            Id = "conversation",
            DialogId = Dialog,
            TraderId = Trader,
        };
        check(StoryRules.EligibleLines(definition, state, facts).Count == 1, "Eligible dialogue choice returned");
        state.Conversation.Closed = true;
        check(StoryRules.EligibleLines(definition, state, facts).Count == 0, "Closed conversations expose no actions");
        state.Conversation.Closed = false;
        var gate = new StoryRandomGate
        {
            VariableId = "random",
            Group = 3,
            Start = 5,
            End = 7,
            Maximum = 10,
        };
        check(!StoryRules.RandomMatches(gate, state.Conversation), "Unseeded random selection fails closed");
        state.Conversation.RandomValues["random:3"] = 7;
        check(StoryRules.RandomMatches(gate, state.Conversation), "Random interval includes endpoint");
        state.Conversation.RandomValues["random:3"] = 8;
        check(!StoryRules.RandomMatches(gate, state.Conversation), "Random interval rejects next branch");
        foreach (var scope in new[] { StoryVariableScope.Session, StoryVariableScope.Dialogue })
        {
            definition.Variables[0].Scope = scope;
            check(StoryRules.Variable(definition, state, facts, Variable) == 0, "Transient variable does not read profile scope: " + scope);
        }
        definition.Variables[0].Scope = StoryVariableScope.Profile;
        check(JsonConvert.SerializeObject(StoryActionType.AcceptQuest) == "\"AcceptQuest\"", "Dialogue enum serializes by meaning");
        Reject(() => JsonConvert.DeserializeObject<StoryAction>("{\"Type\":8}"), "Numeric dialogue enum cannot silently change meaning");
        Reject(() => Evaluate(new() { Type = "Unknown" }), "Unknown conditions cannot silently unlock content");
        Reject(() => Evaluate(new() { Type = "VariableValue", Target = "unknown" }), "Undeclared variables fail closed");
        foreach (
            var corrupt in new Action<StoryDefinition>[]
            {
                d => d.EntryPoints[0].DialogId = "missing",
                d => d.Dialogs[0].Lines[0].Actions[0].Scope = StoryVariableScope.Session,
                d => d.Chapters.Add(SeasonCompiler.Copy(d.Chapters[0])),
                d => d.Dialogs[0].Lines[0].Trigger.Type = "Unknown",
                d => d.Notes[0].ChapterId = "missing",
                d =>
                    d.Dialogs[0]
                        .Lines[0]
                        .Playback.Animations.Add(
                            new()
                            {
                                Key = "Idle",
                                Start = 10,
                                End = 5,
                            }
                        ),
            }
        )
        {
            copy = SeasonCompiler.Copy(season);
            corrupt(copy.Story!);
            report = new();
            StoryValidator.Validate(copy, report);
            check(!report.CanPublish, "Malformed story cannot publish");
        }
        var roundtrip = JsonConvert.DeserializeObject<StoryProgress>(JsonConvert.SerializeObject(state))!;
        check(
            roundtrip.Variables[Variable] == 2 && roundtrip.Conversation!.DialogId == Dialog,
            "Story checkpoint serialization survives restart"
        );
        void Reject(System.Action action, string name)
        {
            var rejected = false;
            try
            {
                action();
            }
            catch (Exception e) when (e is JsonException or InvalidOperationException)
            {
                rejected = true;
            }
            check(rejected, name);
        }
    }

    private static StoryDefinition Definition()
    {
        return new()
        {
            Chapters = [new() { Id = Chapter, Name = "Synthetic chapter" }],
            Quests = [new() { QuestId = Quest, ChapterId = Chapter }],
            Notes =
            [
                new()
                {
                    Id = Note,
                    ChapterId = Chapter,
                    Text = "Synthetic journal entry.",
                },
            ],
            Variables = [new() { Id = Variable }],
            Dialogs =
            [
                new()
                {
                    Id = Dialog,
                    TraderId = Trader,
                    MainVariable = Variable,
                    Lines =
                    [
                        new()
                        {
                            Id = Line,
                            Text = "Continue the synthetic story.",
                            Actions =
                            [
                                new()
                                {
                                    Id = Action,
                                    Type = StoryActionType.SetVariable,
                                    Target = Variable,
                                    Value = 1,
                                },
                            ],
                        },
                    ],
                },
            ],
            EntryPoints =
            [
                new()
                {
                    Id = Entry,
                    TraderId = Trader,
                    DialogId = Dialog,
                },
            ],
        };
    }
}
