using Cysharp.Threading.Tasks;
using UnityEngine.UI;
using WTT.Campaigns.Client.Profiles;
using WTT.Campaigns.Shared.Contracts;

namespace WTT.Campaigns.Client.UI;

public sealed partial class SeasonUi
{
    private CampaignLoadingScreen? _switchLoader;
    private CancellationTokenSource? _switchLoadingCancellation;

    private async UniTask ShowSwitchLoader(CharacterSummary<CharacterVisual> character)
    {
        HideSwitchLoader();
        _switchLoader = new CampaignLoadingScreen(_screen!.Root.GetComponentInChildren<Text>(true).font);
        using var frame = new CancellationTokenSource();
        _switchLoadingCancellation = frame;
        try
        {
            var seasonal = character.Mode == "seasonal";
            var detail = seasonal
                ? "PVE CAMPAIGN"
                    + (
                        string.IsNullOrWhiteSpace(character.SeasonName)
                            ? ""
                            : "  �  " + WTT.Campaigns.Shared.Presentation.CampaignText.Display(character.SeasonName).ToUpperInvariant()
                    )
                : "PVE ZONE  �  REGULAR";
            await _switchLoader.Show(
                "LOADING CHARACTER",
                string.IsNullOrWhiteSpace(character.Name) ? "CHARACTER" : character.Name,
                detail,
                frame.Token
            );
        }
        finally
        {
            if (ReferenceEquals(_switchLoadingCancellation, frame))
                _switchLoadingCancellation = null;
        }
    }

    private void HideSwitchLoader()
    {
        _switchLoadingCancellation?.Cancel();
        _switchLoadingCancellation = null;
        _switchLoader?.Dispose();
        _switchLoader = null;
    }
}
