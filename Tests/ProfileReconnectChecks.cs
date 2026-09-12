using Newtonsoft.Json;
using SPTarkov.Server.Core.Models.Eft.Profile;
using WTT.Campaigns.Client.Profiles;
using WTT.Campaigns.Server.Profiles;
using WTT.Campaigns.Shared.Contracts;

namespace WTT.Campaigns.Tests;

internal static class ProfileReconnectChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        var main = new SptProfile
        {
            ProfileInfo = new() { IsWiped = true },
            CharacterData = new()
            {
                PmcData = new()
                {
                    Info = new() { Nickname = "dev", Level = 42 },
                },
            },
        };
        var campaign = new SptProfile
        {
            ProfileInfo = new() { IsWiped = false },
            CharacterData = new()
            {
                PmcData = new()
                {
                    Info = new() { Nickname = "Campaign", Level = 25 },
                },
            },
        };
        var savedMain = JsonConvert.SerializeObject(main);
        var savedCampaign = JsonConvert.SerializeObject(campaign);
        check(
            main.CharacterData.PmcData.Info != null && ProfileReadiness.PlayablePmc(main) == null,
            "Launcher wipe flag overrides retained nickname and character data"
        );
        check(
            ReferenceEquals(ProfileReadiness.PlayablePmc(campaign), campaign.CharacterData.PmcData),
            "Wiping the main profile leaves campaign readiness independent"
        );
        check(ProfileReadiness.PlayablePmc(null) == null, "Missing profile has no playable character");
        var snapshot = new Snapshot<object>
        {
            ActiveMode = "normal",
            EffectiveProfileId = "main",
            Characters =
            [
                new()
                {
                    Id = "main",
                    Mode = "normal",
                    Exists = ProfileReadiness.PlayablePmc(main)?.Info != null,
                },
                new()
                {
                    Id = "campaign",
                    Mode = "seasonal",
                    Exists = true,
                    Level = 42,
                },
                new()
                {
                    Id = "wiped-campaign",
                    Mode = "seasonal",
                    Wiped = true,
                },
            ],
        };
        var before = JsonConvert.SerializeObject(snapshot);
        var visible = true;
        var setup = new TaskCompletionSource();
        var resumed = false;
        async Task Manage()
        {
            await ProfileReconnect.Run(
                snapshot,
                value => visible = value,
                () =>
                {
                    check(!visible, "Native main-profile setup receives input before reconnect starts");
                    return setup.Task;
                }
            );
            resumed = true;
        }
        var pending = Manage();
        check(!pending.IsCompleted && !resumed && !visible, "Campaign action waits with the native setup screen accessible");
        setup.SetResult();
        await pending;
        check(resumed && visible, "Confirmed character management resumes after native setup and restores the selector");
        check(JsonConvert.SerializeObject(snapshot) == before, "Main-profile setup handoff preserves campaign cards and snapshot state");
        check(
            JsonConvert.SerializeObject(main) == savedMain && JsonConvert.SerializeObject(campaign) == savedCampaign,
            "Readiness and reconnect do not mutate retained main or campaign progression"
        );
        main.ProfileInfo.IsWiped = false;
        check(
            ReferenceEquals(ProfileReadiness.PlayablePmc(main), main.CharacterData.PmcData),
            "Completed native recreation makes the main character selectable again"
        );

        foreach (var failure in new Exception[] { new InvalidOperationException("setup failed"), new OperationCanceledException() })
        {
            try
            {
                await ProfileReconnect.Run(snapshot, value => visible = value, () => Task.FromException(failure));
                throw new Exception("Reconnect failure was swallowed");
            }
            catch (Exception error) when (ReferenceEquals(error, failure))
            {
                check(visible, "Failed or cancelled native setup restores campaign UI for recovery");
            }
        }
        try
        {
            await ProfileReconnect.Run<object>(snapshot, value => visible = value, () => throw new IOException("reconnect failed"));
        }
        catch (IOException)
        {
            check(visible, "Synchronous reconnect failure also restores the selector");
        }

        async Task UnchangedOverlay(string name)
        {
            var calls = 0;
            var connections = 0;
            await ProfileReconnect.Run(
                snapshot,
                _ => calls++,
                () =>
                {
                    connections++;
                    return Task.CompletedTask;
                }
            );
            check(calls == 0 && connections == 1, name);
        }
        snapshot.EffectiveProfileId = "campaign";
        snapshot.ActiveMode = "seasonal";
        await UnchangedOverlay("An empty main profile does not interrupt campaign reconnects");
        snapshot.EffectiveProfileId = "wiped-campaign";
        await UnchangedOverlay("Campaign recreation does not open native main-profile setup");
        snapshot.EffectiveProfileId = "main";
        snapshot.ActiveMode = "normal";
        snapshot.Characters[0].Exists = true;
        await UnchangedOverlay("An existing main profile keeps normal loading presentation");
        snapshot.Characters.Clear();
        await UnchangedOverlay("Missing card metadata is not treated as a launcher wipe");
    }
}
