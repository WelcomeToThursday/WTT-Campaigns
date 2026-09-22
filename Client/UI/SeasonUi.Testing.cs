using WTT.Campaigns.Client.Authoring;
using WTT.Campaigns.UI.Models;
using ZLinq;

namespace WTT.Campaigns.Client.UI;

public sealed partial class SeasonUi
{
    private async void ChooseTestDraft()
    {
        if (_screen == null || Plugin.Busy || Plugin.InRaid)
            return;
        _screen.SetBusy(true, "Loading saved drafts…");
        try
        {
            var drafts = await CampaignTestMode.Drafts();
            _screen.SetBusy(false);
            _screen.ShowTestDrafts(
                drafts
                    .AsValueEnumerable()
                    .Select(d => new SeasonEntry
                    {
                        Id = d.Id,
                        Name = d.Name,
                        Description =
                            $"Saved revision {d.Revision} · "
                            + (d.HasProgress ? "Continue saved test character" : "Start a new test character"),
                    })
                    .ToArray(),
                async id =>
                {
                    CloseForNavigation();
                    try
                    {
                        await CampaignTestMode.Enter("", id, "", drafts.AsValueEnumerable().First(d => d.Id == id).Revision);
                    }
                    catch (Exception e)
                    {
                        Plugin.Error(e);
                        Open();
                        _screen?.SetMessage(e.Message, true);
                    }
                }
            );
        }
        catch (Exception e)
        {
            Plugin.Error(e);
            _screen.SetBusy(false);
            _screen.SetMessage(e.Message, true);
        }
    }
}
