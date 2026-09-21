using Newtonsoft.Json;
using WTT.Campaigns.Client.Missions;
using WTT.Campaigns.Shared.Missions;
using WTT.Campaigns.Shared.Seasons;

namespace WTT.Campaigns.Tests;

internal static class MissionEnvironmentChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var legacy = JsonConvert.DeserializeObject<MissionDefinition>("{\"Id\":\"old\"}")!;
        check(
            legacy.Environment == null && !JsonConvert.SerializeObject(legacy).Contains("Environment"),
            "Legacy missions retain native conditions without changing serialized identity"
        );
        var mission = new MissionDefinition
        {
            Environment = new()
            {
                StartMinutes = 1439,
                Weather = new()
                {
                    Rain = 100,
                    Fog = 25,
                    Direction = 8,
                },
            },
        };
        var copy = SeasonCompiler.Copy(new MissionDescriptor { Definition = mission });
        check(
            copy.Definition.Environment!.StartMinutes == 1439 && copy.Definition.Environment.Weather!.Direction == 8,
            "Server descriptor serialization preserves time and weather"
        );
        check(MissionLogicRules.DraftErrors(mission).Count == 0, "Valid environment saves with a mission draft");
        foreach (var minutes in new[] { -1, 1440 })
        {
            mission.Environment.StartMinutes = minutes;
            check(MissionLogicRules.DraftErrors(mission).Count > 0, "Draft save rejects invalid start minutes");
        }
        mission.Environment.StartMinutes = null;
        mission.Environment.FreezeTime = true;
        check(MissionLogicRules.DraftErrors(mission).Count > 0, "Frozen time needs a start time");
        mission.Environment.FreezeTime = false;
        foreach (var value in new[] { -1, 101, float.NaN, float.PositiveInfinity })
        {
            mission.Environment.Weather!.Rain = value;
            check(MissionLogicRules.DraftErrors(mission).Count > 0, "Invalid weather is rejected at the shared draft boundary");
        }
        mission.Environment.Weather!.Rain = 0;
        mission.Environment.Weather.Direction = 0;
        check(MissionLogicRules.DraftErrors(mission).Count > 0, "Undefined wind direction is rejected");

        var sky = TODSkyProvider.Instance = new();
        var date = new DateTime(2026, 9, 21, 23, 59, 0, DateTimeKind.Utc);
        UnityEngine.Time.realtimeSinceStartup = 100;
        sky.Cycle.DateTime = date;
        var clock = sky.CurrentTime.GameDateTime!;
        clock.ResetForce(date);
        var checkpoint = new MissionTimeSnapshot();
        for (var retry = 0; retry < 3; retry++)
        {
            UnityEngine.Time.realtimeSinceStartup += 120;
            sky.Cycle.DateTime = clock.Calculate();
            check(sky.Cycle.DateTime.Day > date.Day, "Failed attempt crosses midnight");
            checkpoint.Restore();
            check(
                clock.Calculate() == date && sky.Cycle.DateTime == date && clock.Locked,
                "Retry restores native and displayed date without unlocking the native clock"
            );
            UnityEngine.Time.realtimeSinceStartup += 1;
            check(clock.Calculate() == date.AddSeconds(7), "Time continues at the original native rate after retry");
        }
        sky.CurrentTime.LockCurrentTime = true;
        clock.TimeFactorMod = 0;
        var frozenDate = clock.Calculate();
        var frozen = new MissionTimeSnapshot();
        UnityEngine.Time.realtimeSinceStartup += 600;
        frozen.Restore();
        check(
            clock.Calculate() == frozenDate && sky.CurrentTime.LockCurrentTime && clock.TimeFactorMod == 0,
            "Frozen mission time stays frozen after checkpoint retry"
        );
        sky.CurrentTime.LockCurrentTime = false;
        clock.TimeFactorMod = 1;
        clock.ResetForce(date);
        sky.Cycle.DateTime = date;
        var baseline = new MissionTimeSnapshot();
        UnityEngine.Time.realtimeSinceStartup += 60;
        clock.ResetForce(date.AddHours(6));
        baseline.Restore(advance: true);
        check(clock.Calculate() == date.AddSeconds(420), "Ending a playtest restores the underlying advancing raid time");
        sky.CurrentTime = new();
        var rejected = false;
        try
        {
            checkpoint.Restore();
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }
        check(rejected, "Checkpoint cannot rewind another raid's clock");
        TODSkyProvider.IsAvailable = false;
        new MissionTimeSnapshot().Restore();
        TODSkyProvider.IsAvailable = true;
    }
}
