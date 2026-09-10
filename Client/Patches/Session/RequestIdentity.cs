namespace WTT.Campaigns.Client.Patches.Session;

internal static class RequestIdentity
{
    internal static void Apply(bool sharedClient, string path, string? sessionId, HttpRequestMessage request)
    {
        // Account management uses the launcher identity. Raid documents belong to the
        // active backend character, just like the native raid start/end requests.
        if (
            !sharedClient
            || (path.StartsWith("/wtt-campaigns/", StringComparison.Ordinal) && path != "/wtt-campaigns/hub/raid-document")
            || string.IsNullOrEmpty(sessionId)
        )
        {
            return;
        }

        // Capture identity now so a later character switch cannot retarget an in-flight request.
        request.Headers.Remove("Cookie");
        request.Headers.Add("Cookie", "PHPSESSID=" + sessionId);
    }
}
