namespace WTT.Campaigns.Shared.Authoring;

public static class CampaignTestPolicy
{
    public static void RequireRequest(CampaignTestRequest request, params string[] actions)
    {
        if (request.Version != 2)
            throw new InvalidOperationException("Update both components together (campaign test protocol 2 required).");
        if (Array.IndexOf(actions, request.Action) < 0)
            throw new InvalidOperationException("The campaign test action does not match its endpoint.");
    }

    public static void RequireUpdate(CampaignTestRequest request, long loaded, long latest, bool inRaid)
    {
        if (inRaid)
            throw new InvalidOperationException("Finish the raid before updating a campaign test.");
        if (!Guid.TryParseExact(request.OperationId, "N", out _))
            throw new InvalidOperationException("A campaign test operation identity is required.");
        if (request.ExpectedLoadedRevision != loaded || request.ExpectedDraftRevision != latest)
            throw new InvalidOperationException("The test or saved draft changed. Refresh before trying again.");
    }
}
