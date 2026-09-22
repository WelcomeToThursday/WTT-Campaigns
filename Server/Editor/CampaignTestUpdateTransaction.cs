namespace WTT.Campaigns.Server.Editor;

public static class CampaignTestUpdateTransaction
{
    public static void Commit(Action install, Action persist, Action restore)
    {
        try
        {
            install();
            persist();
        }
        catch (Exception failure)
        {
            try
            {
                restore();
            }
            catch (Exception recovery)
            {
                throw new AggregateException(
                    "The test could not be refreshed or restored. Its last saved snapshot is retained; return and resume it.",
                    failure,
                    recovery
                );
            }
            throw;
        }
    }
}
