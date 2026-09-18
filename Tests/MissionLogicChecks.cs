using Newtonsoft.Json;
using WTT.Campaigns.Shared.Missions;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Tests;

internal static class MissionLogicChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var layout = new MapLayout
        {
            Checkpoints = [new() { Id = "checkpoint" }],
            Exit = new() { Id = "exit" },
            Encounters =
            [
                new()
                {
                    Id = "guards",
                    Trigger = new() { Type = MapEncounterTrigger.Event, EventId = "alarm" },
                    Waves = [new() { Id = "wave", Roster = [new() { Id = "guard", Count = 1 }] }],
                },
            ],
        };
        var kill = new MissionObjective
        {
            Id = "kill",
            Name = "Clear guard",
            Type = MissionObjective.Target,
            TargetKind = "Roster",
            TargetIds = ["guard"],
        };
        var mission = new MissionDefinition
        {
            Objectives = [kill],
            Events =
            [
                new()
                {
                    Id = "start",
                    Source = MissionSignals.Start,
                    Actions = [new() { Type = MissionAction.Encounter, TargetId = "guards" }],
                },
            ],
        };
        var state = new MissionLogicState();
        void Apply(string kind, double time = 0, string id = "", string profile = "") =>
            MissionLogic.Apply(
                mission,
                layout,
                state,
                new()
                {
                    Kind = kind,
                    Time = time,
                    TargetId = id,
                    ProfileId = profile,
                }
            );
        check(MissionLogicRules.Errors(mission, layout).Count == 0, "Valid mission event and single actor objective are accepted");
        Apply(MissionSignals.Start);
        Apply(MissionSignals.Start);
        check(state.FiredRules.Count == 1 && state.ActivatedEncounters.SetEquals(["guards"]), "Repeated source only fires a rule once");
        check(!MissionLogic.CanAdvance(mission, state, "", out _), "Required kill objective blocks extraction before spawning");
        state.Actors["native"] = new()
        {
            ProfileId = "native",
            EncounterId = "guards",
            WaveId = "wave",
            RosterId = "guard",
        };
        Throws(() => Apply(MissionSignals.Death, profile: "native"), "An unspawned actor cannot be killed");
        Apply(MissionSignals.Spawn, profile: "native");
        check(!MissionLogic.CanAdvance(mission, state, "", out _), "Living target blocks extraction");
        Apply(MissionSignals.Death, 1, profile: "native");
        Apply(MissionSignals.Death, 1, profile: "native");
        check(
            state.Objectives["kill"].Count == 1 && MissionLogic.CanAdvance(mission, state, "", out _),
            "Confirmed death counts once and unlocks extraction"
        );
        var checkpoint = JsonConvert.SerializeObject(state);
        var copy = JsonConvert.DeserializeObject<MissionLogicState>(checkpoint)!;
        check(copy.Actors["native"].Dead && copy.FiredRules.Contains("start"), "Checkpoint state round trips actor and event identities");

        mission = new()
        {
            Objectives =
            [
                new()
                {
                    Id = "defend",
                    Name = "Defend",
                    Type = MissionObjective.Defend,
                    TargetIds = ["guards"],
                    ZoneId = "checkpoint",
                    Seconds = 10,
                },
            ],
        };
        state = new();
        Apply(MissionSignals.Start);
        MissionLogic.Apply(
            mission,
            layout,
            state,
            new()
            {
                Kind = MissionSignals.Sample,
                TargetId = "checkpoint",
                PlayerInside = true,
                Time = 2,
            }
        );
        Apply(MissionSignals.Tick, 6);
        check(state.Objectives["defend"].Seconds == 4, "Defend starts credit at entry, not before");
        state.Actors["enemy"] = new()
        {
            ProfileId = "enemy",
            EncounterId = "guards",
            Spawned = true,
        };
        MissionLogic.Apply(
            mission,
            layout,
            state,
            new()
            {
                Kind = MissionSignals.Sample,
                TargetId = "checkpoint",
                PlayerInside = true,
                Occupants = ["enemy"],
                Time = 6,
            }
        );
        Apply(MissionSignals.Tick, 12);
        check(state.Objectives["defend"].Seconds == 4, "Contested defend pauses without resetting");
        MissionLogic.Apply(
            mission,
            layout,
            state,
            new()
            {
                Kind = MissionSignals.Sample,
                TargetId = "checkpoint",
                PlayerInside = true,
                Time = 12,
            }
        );
        Apply(MissionSignals.Tick, 18);
        check(state.Objectives["defend"].Status == "Completed", "Defend resumes to completion");

        mission = new()
        {
            Objectives =
            [
                new()
                {
                    Id = "protect",
                    Name = "Protect guard",
                    Type = MissionObjective.Protect,
                    TargetKind = "Roster",
                    TargetIds = ["guard"],
                },
            ],
        };
        state = new();
        Apply(MissionSignals.Start);
        check(!MissionLogic.CanAdvance(mission, state, "", out _), "Protect cannot complete before actor spawns");
        state.Actors["native"] = new()
        {
            ProfileId = "native",
            RosterId = "guard",
            Spawned = true,
        };
        check(MissionLogic.CanAdvance(mission, state, "", out _), "Living protected actor permits exit without circular gating");
        Apply(MissionSignals.Exit);
        check(state.Objectives["protect"].Status == "Completed", "Exit completes protect-until-exit");
        state = new();
        Apply(MissionSignals.Start);
        state.Actors["native"] = new()
        {
            ProfileId = "native",
            RosterId = "guard",
            Spawned = true,
        };
        Apply(MissionSignals.Death, profile: "native");
        check(
            state.Failure.Length > 0 && !MissionLogic.CanAdvance(mission, state, "", out _),
            "Protected actor death fails required objective"
        );

        mission = new()
        {
            Events =
            [
                new()
                {
                    Id = "a",
                    Source = MissionSignals.Timer,
                    SourceId = "timer",
                    Actions =
                    [
                        new()
                        {
                            Type = MissionAction.Timer,
                            TargetId = "timer",
                            Seconds = 1,
                        },
                    ],
                },
            ],
        };
        check(MissionLogicRules.Errors(mission, layout).Any(e => e.Contains("circular")), "Timer dependency cycle is rejected");
        mission.Events[0].Source = MissionSignals.Start;
        mission.Events[0].SourceId = "";
        state = new();
        Apply(MissionSignals.Start);
        Apply(MissionSignals.Tick, 1);
        check(state.FinishedTimers.Contains("timer") && state.Timers.Count == 0, "Timer elapses once");
        mission.Events[0].Actions[0].TargetId = "";
        check(MissionLogicRules.Errors(mission, layout).Count > 0, "Missing action reference is rejected");
        var run = new MissionRun();
        Throws(
            () =>
                MissionObservationRules.Apply(
                    new(),
                    layout,
                    run,
                    new() { AttemptGeneration = 2, Signals = [new() { Kind = MissionSignals.Tick }] },
                    10
                ),
            "Retired attempt cannot report observations"
        );
        Throws(
            () =>
                MissionObservationRules.Apply(
                    new(),
                    layout,
                    run,
                    new() { Signals = [new() { Kind = MissionSignals.Complete, TargetId = "kill" }] },
                    10
                ),
            "Client cannot assert objective completion"
        );
        Throws(
            () =>
                MissionObservationRules.Apply(
                    new(),
                    layout,
                    run,
                    new() { Signals = [new() { Kind = MissionSignals.Encounter, TargetId = "guards" }] },
                    10
                ),
            "Missing actors cannot assert encounter completion"
        );
        Throws(
            () =>
                MissionObservationRules.Apply(
                    new(),
                    layout,
                    run,
                    new() { Signals = [new() { Kind = MissionSignals.Tick, Time = 100 }] },
                    10
                ),
            "Future observations are rejected"
        );

        mission = new() { Objectives = [kill], Requirements = [new() { CheckpointId = "checkpoint", ObjectiveIds = ["kill"] }] };
        state = new();
        Apply(MissionSignals.Start);
        check(!MissionLogic.CanAdvance(mission, state, "checkpoint", out _), "Required objective gates its selected checkpoint");
        check(MissionLogic.CanAdvance(mission, state, "another", out _), "Objective gate does not attach to unrelated checkpoints");
        kill.Required = false;
        check(
            MissionLogic.CanAdvance(mission, state, "checkpoint", out _) && MissionLogic.CanAdvance(mission, state, "", out _),
            "Optional objectives never block checkpoint or exit"
        );
        kill.Required = true;
        state.Actors["native"] = new()
        {
            ProfileId = "native",
            EncounterId = "guards",
            RosterId = "guard",
            Spawned = true,
            Dead = true,
        };
        kill.OnStart = false;
        mission.Events =
        [
            new()
            {
                Id = "activate",
                Source = MissionSignals.Checkpoint,
                SourceId = "checkpoint",
                Actions = [new() { Type = MissionAction.Objective, TargetId = "kill" }],
            },
        ];
        state.Objectives.Clear();
        Apply(MissionSignals.Checkpoint, id: "checkpoint");
        check(state.Objectives["kill"].Status == "Completed", "Delayed elimination objective recognizes earlier confirmed death");
        check(
            MissionLogicRules.Errors(mission, layout).Any(e => e.Contains("circular")),
            "Checkpoint cannot activate the objective required to reach itself"
        );
        kill.OnStart = true;

        mission = new()
        {
            Objectives =
            [
                new()
                {
                    Id = "survive",
                    Type = MissionObjective.Survive,
                    TargetIds = ["guards"],
                },
            ],
        };
        state = new();
        Apply(MissionSignals.Start);
        Apply(MissionSignals.Wave, id: "wave");
        check(state.Objectives["survive"].Status == "Active", "A single wave notification does not complete an entire encounter objective");
        Apply(MissionSignals.Encounter, id: "guards");
        check(state.Objectives["survive"].Status == "Completed", "Survive waves completes when all selected encounters finish");
        var oldMission = JsonConvert.DeserializeObject<MissionDefinition>("{\"Id\":\"legacy\"}")!;
        var unfinished = new MissionDefinition
        {
            Objectives = [new() { Id = "target", Type = MissionObjective.Target }],
            Events =
            [
                new()
                {
                    Id = "unfinished",
                    Source = MissionSignals.Complete,
                    Actions = [new()],
                },
            ],
        };
        check(
            MissionLogicRules.DraftErrors(unfinished).Count == 0,
            "Unfinished objective and event selections may be synchronized as drafts"
        );
        check(MissionLogicRules.Errors(unfinished, layout).Count > 0, "Unfinished mission logic still blocks publication and playtests");
        unfinished.Objectives.Add(new() { Id = "target" });
        check(MissionLogicRules.DraftErrors(unfinished).Count > 0, "Draft synchronization retains unique record identity validation");
        using (
            var server = Mono.Cecil.AssemblyDefinition.ReadAssembly(
                typeof(WTT.Campaigns.Server.Web.Authoring.RaidAuthoringService).Assembly.Location
            )
        )
        {
            var service = server.MainModule.Types.Single(t => t.Name == "RaidAuthoringService");
            IEnumerable<Mono.Cecil.TypeDefinition> Types(Mono.Cecil.TypeDefinition type)
            {
                yield return type;
                foreach (var nested in type.NestedTypes.SelectMany(Types))
                    yield return nested;
            }
            var validators = Types(service)
                .SelectMany(t => t.Methods)
                .Where(m => m.HasBody)
                .SelectMany(m => m.Body.Instructions)
                .Select(i => i.Operand)
                .OfType<Mono.Cecil.MethodReference>()
                .Where(m => m.DeclaringType.FullName == typeof(MissionLogicRules).FullName)
                .Select(m => m.Name)
                .ToArray();
            check(
                validators.Contains(nameof(MissionLogicRules.DraftErrors)) && !validators.Contains(nameof(MissionLogicRules.Errors)),
                "Actual raid authoring save applies draft structural validation rather than publication readiness"
            );
        }
        check(
            !MissionLogic.HasLogic(oldMission) && !oldMission.CheckpointRetries,
            "Legacy missions retain route-only behavior and retries disabled"
        );
        check(
            MissionLogicRules.Errors(new() { CheckpointRetries = true }, layout).Count == 0,
            "Native checkpoint retries can be explicitly published"
        );
        mission = new()
        {
            Objectives = [kill],
            Events =
            [
                new()
                {
                    Id = "event",
                    Source = MissionSignals.Complete,
                    SourceId = "kill",
                    Actions =
                    [
                        new()
                        {
                            Type = MissionAction.Timer,
                            TargetId = "after",
                            Seconds = 1,
                        },
                    ],
                },
            ],
            Requirements = [new() { CheckpointId = "checkpoint", ObjectiveIds = ["kill"] }],
        };
        MissionAuthoring.Remap(
            mission,
            new Dictionary<string, string>
            {
                ["kill"] = "new-kill",
                ["event"] = "new-event",
                ["guard"] = "new-guard",
                ["checkpoint"] = "new-checkpoint",
            }
        );
        check(
            mission.Objectives[0].Id == "new-kill"
                && mission.Events[0].SourceId == "new-kill"
                && mission.Requirements[0].ObjectiveIds[0] == "new-kill"
                && mission.Objectives[0].TargetIds[0] == "new-guard",
            "Duplication remaps objective targets, event references and gates together"
        );

        void Throws(Action action, string message)
        {
            try
            {
                action();
                check(false, message);
            }
            catch (InvalidOperationException)
            {
                check(true, message);
            }
        }
    }
}
