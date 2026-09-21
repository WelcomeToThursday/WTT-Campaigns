using Comfort.Common;
using EFT;
using Newtonsoft.Json;
using WTT.Campaigns.Client.Missions;
using WTT.Campaigns.Shared.Missions;
using WTT.Campaigns.Shared.Seasons;

namespace WTT.Campaigns.Tests;

internal static class MissionTimerChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var legacy = JsonConvert.DeserializeObject<MissionDefinition>("{\"Id\":\"old\"}")!;
        check(
            legacy.TimeLimitMinutes == null && !JsonConvert.SerializeObject(legacy).Contains("TimeLimitMinutes"),
            "Old missions inherit map time without changing serialized identity"
        );
        foreach (int? minutes in new int?[] { null, 0, 1, 30, 1440 })
        {
            var mission = new MissionDefinition { TimeLimitMinutes = minutes };
            var descriptor = SeasonCompiler.Copy(new MissionDescriptor { Definition = mission });
            check(
                descriptor.Definition.TimeLimitMinutes == minutes,
                "Server descriptor preserves mission timer mode and duration: " + minutes
            );
            check(MissionLogicRules.DraftErrors(mission).Count == 0, "Valid mission timer accepted: " + minutes);
        }
        foreach (var minutes in new[] { -1, 1441, int.MaxValue })
            check(
                MissionLogicRules.DraftErrors(new() { TimeLimitMinutes = minutes }).Count != 0,
                "Invalid mission timer cannot be saved or published"
            );

        UnityEngine.Time.realtimeSinceStartup = 0;
        var game = Singleton<AbstractGame>.Instance = new();
        var originalSession = game.GameTimer.SessionTime;
        var originalEscape = game.GameTimer.EscapeDateTime;
        using (var timer = new MissionRaidTimer(2, rehearsal: false))
        {
            check(
                game.GameTimer.SessionTime == null && timer.Remaining == 120,
                "Mission preparation pauses native expiry without spending authored time"
            );
            UnityEngine.Time.realtimeSinceStartup = 30;
            timer.Resume();
            check(
                game.GameTimer.EscapeDateTime == DateTimeExtensions.UtcNow.AddSeconds(120),
                "Timed mission sets the actual native expiry, not only its label"
            );
            UnityEngine.Time.realtimeSinceStartup += 40;
            timer.Pause();
            var snapshot = timer.Capture();
            check(
                timer.Remaining == 80 && game.GameTimer.SessionTime == null,
                "Checkpoint captures remaining time and pauses native expiry"
            );
            UnityEngine.Time.realtimeSinceStartup += 1000;
            check(timer.Remaining == 80 && !timer.Expired, "Retry menus and checkpoint operations do not consume time");
            timer.Resume();
            UnityEngine.Time.realtimeSinceStartup += 75;
            check(timer.Remaining == 5, "Resumed attempt spends its remaining time");
            timer.Pause();
            snapshot.Restore();
            timer.Resume();
            check(
                timer.Remaining == 80 && game.GameTimer.EscapeDateTime == DateTimeExtensions.UtcNow.AddSeconds(80),
                "Retry restores checkpoint time to both HUD and native expiry"
            );
            UnityEngine.Time.realtimeSinceStartup += 80;
            check(timer.Expired && MissionCountdown.Text(timer.Remaining) == "0:00:00", "Timed mission reaches zero exactly");
        }
        check(
            game.GameTimer.SessionTime == originalSession && game.GameTimer.EscapeDateTime == originalEscape,
            "Timer ownership restores original raid state on disposal"
        );

        game = Singleton<AbstractGame>.Instance = new();
        using (var infinite = new MissionRaidTimer(0, rehearsal: false))
        {
            infinite.Resume();
            UnityEngine.Time.realtimeSinceStartup += 1_000_000;
            check(
                infinite.Remaining == null
                    && !infinite.Expired
                    && game.GameTimer.SessionTime == null
                    && game.GameTimer.EscapeDateTime == null,
                "Infinite has no native deadline and never expires"
            );
            check(
                MissionCountdown.Text(infinite.Remaining) == "∞",
                "Infinite displays the infinity symbol rather than zero or a large countdown"
            );
            infinite.Pause();
            var saved = infinite.Capture();
            saved.Restore();
            infinite.Resume();
            check(infinite.Remaining == null, "Checkpoint restoration preserves infinite mode");
        }
        game = Singleton<AbstractGame>.Instance = new();
        UnityEngine.Time.realtimeSinceStartup += 90;
        using (var inherited = new MissionRaidTimer(null, rehearsal: false))
        {
            check(inherited.Remaining == 45 * 60 - 90, "Map default inherits the existing raid's remaining time");
        }
        UnityEngine.Time.realtimeSinceStartup += 3600;
        using (var inheritedPreview = new MissionRaidTimer(null, rehearsal: true))
        {
            check(inheritedPreview.Remaining == 45 * 60, "Map-default playtests receive a full duration even after a long editing session");
        }
        using (var preview = new MissionRaidTimer(1, rehearsal: true))
        {
            preview.Resume();
            UnityEngine.Time.realtimeSinceStartup += 61;
            check(preview.Expired && game.GameTimer.SessionTime == null, "Playtest can expire without ending the native editor raid");
            var saved = preview.Capture();
            Singleton<AbstractGame>.Instance = new();
            var rejected = false;
            try
            {
                saved.Restore();
            }
            catch (InvalidOperationException)
            {
                rejected = true;
            }
            check(rejected, "Old checkpoint timer cannot alter a replacement raid");
        }
        check(MissionRaidTimer.Current == null, "Timer ownership is cleared after teardown");
        check(
            MissionCountdown.Text(3600) == "1:00:00" && MissionCountdown.Text(.2) == "0:00:01",
            "Countdown hours and final partial second stay readable"
        );
        Singleton<AbstractGame>.Instance = null!;
    }
}
