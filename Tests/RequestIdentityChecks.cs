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
        HttpRequestMessage Create(string path, string? sessionId = character, bool shared = true)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost" + path);
            request.Headers.Add("Cookie", "PHPSESSID=" + root);
            RequestIdentity.Apply(shared, path, sessionId, request);
            return request;
        }
        string Cookie(HttpRequestMessage request)
        {
            return string.Join(";", request.Headers.GetValues("Cookie"));
        }

        using var pickup = Create(documents);
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
    }
}
