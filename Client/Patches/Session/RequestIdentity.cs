namespace WTT.Campaigns.Client.Patches.Session;

internal static class RequestIdentity
{
    internal const string MissionRunHeader = "X-WTT-Mission-Run";

    internal static void ApplyNativeMissionMarker(string path, string? characterId, IDictionary<string, string> headers, string runId)
    {
        if (path != "/client/match/local/start")
            return;
        headers.Remove(MissionRunHeader);
        if (string.IsNullOrWhiteSpace(runId) || string.IsNullOrWhiteSpace(characterId))
            return;
        // Keep the native request's authentication. A pending run must never
        // authorize a request belonging to another backend session.
        if (!headers.TryGetValue("Cookie", out var cookie) || cookie != "PHPSESSID=" + characterId)
            throw new InvalidOperationException("The mission launch request belongs to another backend character.");
        headers[MissionRunHeader] = runId;
    }

    internal static void Apply(
        bool sharedClient,
        string path,
        string? sessionId,
        HttpRequestMessage request,
        bool campaignTest = false,
        string missionRunId = ""
    )
    {
        // Account management uses the launcher identity. Raid documents belong to the
        // active backend character, just like the native raid start/end requests.
        if (
            !sharedClient
            || (
                path.StartsWith("/wtt-campaigns/", StringComparison.Ordinal)
                && path != "/wtt-campaigns/hub/raid-document"
                && path != "/wtt-campaigns/appearance"
                && (
                    !campaignTest
                    || path.StartsWith("/wtt-campaigns/editor/", StringComparison.Ordinal)
                    || path.StartsWith("/wtt-campaigns/test-campaign/", StringComparison.Ordinal)
                )
            )
            || string.IsNullOrEmpty(sessionId)
        )
        {
            return;
        }

        // Capture identity now so a later character switch cannot retarget an in-flight request.
        request.Headers.Remove("Cookie");
        request.Headers.Add("Cookie", "PHPSESSID=" + sessionId);
        if (path == "/client/match/local/start")
        {
            request.Headers.Remove("X-WTT-Mission-Run");
            if (!string.IsNullOrWhiteSpace(missionRunId))
                request.Headers.Add("X-WTT-Mission-Run", missionRunId);
        }
    }
}
