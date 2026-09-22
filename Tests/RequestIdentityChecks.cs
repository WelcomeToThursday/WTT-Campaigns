using WTT.Campaigns.Client.Patches.Session;

namespace WTT.Campaigns.Tests;

internal static class RequestIdentityChecks
{
    internal static void Run(Action<bool, string> check)
    {
        const string root = "111111111111111111111111";
        const string character = "222222222222222222222222";
        const string otherCharacter = "333333333333333333333333";
        const string documents = "/wtt-campaigns/hub/raid-document";
        HttpRequestMessage Create(string path, string? sessionId = character, bool shared = true, bool campaignTest = false)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost" + path);
            request.Headers.Add("Cookie", "PHPSESSID=" + root);
            RequestIdentity.Apply(shared, path, sessionId, request, campaignTest);
            return request;
        }
        string Cookie(HttpRequestMessage request)
        {
            return string.Join(";", request.Headers.GetValues("Cookie"));
        }

        using var pickup = Create(documents);
        using var appearance = Create("/wtt-campaigns/appearance");
        check(Cookie(appearance) == "PHPSESSID=" + character, "Appearance saves authenticate as the active character");
        using var appearanceSwitched = Create("/wtt-campaigns/appearance", otherCharacter);
        check(Cookie(appearanceSwitched) == "PHPSESSID=" + otherCharacter, "Appearance saves follow the selected seasonal character");
        check(Cookie(pickup) == "PHPSESSID=" + character, "Raid documents authenticate as the active character, not the launcher account");
        using var retry = Create(documents);
        check(Cookie(retry) == Cookie(pickup), "Persisted document retries use the same active character identity");
        foreach (var path in new[] { "/client/match/local/start", "/client/match/local/end" })
        {
            using var request = Create(path);
            check(Cookie(request) == Cookie(pickup), "Document and native raid requests agree on identity: " + path);
        }
        foreach (
            var path in new[]
            {
                "/wtt-campaigns/snapshot",
                "/wtt-campaigns/switch",
                "/wtt-campaigns/raid-abort",
                "/wtt-campaigns/hub",
                "/wtt-campaigns/hub/claim",
                "/wtt-campaigns/hub/exchange",
                "/wtt-campaigns/editor/status",
                "/wtt-campaigns/editor/preview-gear",
                "/wtt-campaigns/editor/encounter-profiles",
                "/wtt-campaigns/editor/catalogue",
            }
        )
        {
            using var request = Create(path);
            check(Cookie(request) == "PHPSESSID=" + root, "Account-scoped route retains launcher identity: " + path);
        }
        using var independent = Create(documents, shared: false);
        check(Cookie(independent) == "PHPSESSID=" + root, "Independent HTTP clients retain their own identity");
        using var initial = Create(documents, sessionId: null);
        check(Cookie(initial) == "PHPSESSID=" + root, "Requests before backend initialization retain their original identity");
        using var switched = Create(documents, otherCharacter);
        check(Cookie(switched) == "PHPSESSID=" + otherCharacter, "New document requests follow a character switch");
        check(Cookie(pickup) == "PHPSESSID=" + character, "A character switch cannot retarget an existing request");
        foreach (
            var path in new[]
            {
                "/wtt-campaigns/snapshot",
                "/wtt-campaigns/missions/list",
                "/wtt-campaigns/missions/progress",
                "/wtt-campaigns/raid-abort",
                "/wtt-campaigns/hub",
                "/client/quest/accept",
                "/client/match/local/end",
            }
        )
        {
            using var request = Create(path, campaignTest: true);
            check(Cookie(request) == "PHPSESSID=" + character, "Disposable campaign gameplay uses its own identity: " + path);
        }
        foreach (
            var path in new[] { "/wtt-campaigns/editor/begin", "/wtt-campaigns/test-campaign/create", "/wtt-campaigns/test-campaign/end" }
        )
        {
            using var request = Create(path, campaignTest: true);
            check(Cookie(request) == "PHPSESSID=" + root, "Disposable campaign management keeps its authenticated owner: " + path);
        }
        using var missionLaunch = new HttpRequestMessage();
        RequestIdentity.Apply(true, "/client/match/local/start", character, missionLaunch, missionRunId: "mission-run");
        check(
            missionLaunch.Headers.GetValues("X-WTT-Mission-Run").Single() == "mission-run",
            "Explicit native mission launch carries its prepared run identity"
        );
        RequestIdentity.Apply(true, "/client/match/local/start", character, missionLaunch);
        check(!missionLaunch.Headers.Contains("X-WTT-Mission-Run"), "Ordinary native launch clears any previous mission marker");
        using var otherRoute = new HttpRequestMessage();
        RequestIdentity.Apply(true, "/client/items", character, otherRoute, missionRunId: "mission-run");
        check(!otherRoute.Headers.Contains("X-WTT-Mission-Run"), "Mission launch identity is not sent on unrelated requests");

        var native = new Dictionary<string, string> { ["Cookie"] = "PHPSESSID=" + character, ["GClient-RequestId"] = "42" };
        RequestIdentity.ApplyNativeMissionMarker("/client/match/local/start", character, native, "mission-run");
        check(native["X-WTT-Mission-Run"] == "mission-run", "Native EFT transport carries the prepared mission marker");
        check(
            native["Cookie"] == "PHPSESSID=" + character && native["GClient-RequestId"] == "42",
            "Mission marker preserves native authentication and request headers"
        );
        // Feed the header emitted by the native client path into the server's
        // per-request context rather than authorizing a separate test marker.
        var scope = WTT.Campaigns.Server.Missions.MissionLaunchContext.Push(
            character,
            native.ContainsKey(WTT.Campaigns.Server.Missions.MissionLaunchContext.HeaderName),
            native[WTT.Campaigns.Server.Missions.MissionLaunchContext.HeaderName]
        );
        check(
            WTT.Campaigns.Server.Missions.MissionLaunchContext.Current?.RunId == "mission-run"
                && WTT.Campaigns.Server.Missions.MissionLaunchContext.Current.SessionId == character,
            "Native launch header agrees with the server marker and character"
        );
        scope.Restore();
        RequestIdentity.ApplyNativeMissionMarker("/client/match/local/start", character, native, "");
        check(!native.ContainsKey("X-WTT-Mission-Run"), "Ordinary native launch cannot reuse a stale mission marker");
        RequestIdentity.ApplyNativeMissionMarker("/client/match/local/end", character, native, "mission-run");
        check(!native.ContainsKey("X-WTT-Mission-Run"), "Native mission marker is restricted to raid start");
        native["Cookie"] = "PHPSESSID=" + otherCharacter;
        var rejected = false;
        try
        {
            RequestIdentity.ApplyNativeMissionMarker("/client/match/local/start", character, native, "mission-run");
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }
        check(
            rejected && !native.ContainsKey("X-WTT-Mission-Run") && native["Cookie"] == "PHPSESSID=" + otherCharacter,
            "Native launch fails closed when its authentication belongs to another character"
        );
    }
}
