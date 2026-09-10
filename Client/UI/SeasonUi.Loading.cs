using Cysharp.Threading.Tasks;
using EFT.Hideout;
using EFT.UI;
using UnityEngine;
using UnityEngine.UI;
using WTT.Campaigns.Client.Profiles;
using WTT.Campaigns.Shared.Contracts;
using WTT.Campaigns.UI.Controls;

namespace WTT.Campaigns.Client.UI;

public sealed partial class SeasonUi
{
    private PveGameModeLoadingScreen? _switchLoader;
    private Text? _switchLoadingName;
    private Text? _switchLoadingMode;
    private CancellationTokenSource? _switchLoadingCancellation;

    private async UniTask ShowSwitchLoader(CharacterSummary<CharacterVisual> character)
    {
        if (!_switchLoader)
        {
            // Keep the native screen above our cards and independent of the backend's
            // own show/fade calls, so it stays visible until the profile is verified.
            _switchLoader = Instantiate(PreloaderUI.Instance._pveLoadingScreen, _canvas!.transform, false);
            _switchLoader.name = "SeasonalSwitchLoadingScreen";
            UiElements.Stretch((RectTransform)_switchLoader.transform);

            var textRoot = UiElements.Rect("LoadingText", _switchLoader.transform, 0, 0);
            UiElements.Stretch(textRoot);
            var textGroup = textRoot.gameObject.AddComponent<CanvasGroup>();
            textGroup.blocksRaycasts = false;
            textRoot.gameObject.AddComponent<LoadingTextAnimation>().Initialize(_switchLoader._screenAnimator, textGroup);
            var ui = new UiElements(_screen!.Root.GetComponentInChildren<Text>(true).font);
            var caption = ui.Label(textRoot, "LoadingCaption", "LOADING CHARACTER", 14, 760, 24, 0, -130);
            caption.alignment = TextAnchor.MiddleCenter;
            caption.color = new Color(.35f, .69f, .75f, .8f);
            _switchLoadingName = ui.Label(textRoot, "LoadingCharacterName", "", 32, 760, 48, 0, -168);
            _switchLoadingName.alignment = TextAnchor.MiddleCenter;
            _switchLoadingName.color = new Color(.72f, .77f, .78f);
            _switchLoadingName.resizeTextForBestFit = true;
            _switchLoadingName.resizeTextMinSize = 18;
            _switchLoadingName.resizeTextMaxSize = 32;
            _switchLoadingMode = ui.Label(textRoot, "LoadingCharacterMode", "", 18, 900, 54, 0, -215);
            _switchLoadingMode.alignment = TextAnchor.MiddleCenter;
            _switchLoadingMode.color = new Color(.35f, .69f, .75f);
            _switchLoadingMode.resizeTextForBestFit = true;
            _switchLoadingMode.resizeTextMinSize = 14;
            _switchLoadingMode.resizeTextMaxSize = 18;
        }

        // Use the selected card, not the still-loaded profile or current season.
        _switchLoadingName!.text = string.IsNullOrWhiteSpace(character.Name) ? "CHARACTER" : character.Name;
        var seasonal = character.Mode == "seasonal";
        _switchLoadingMode!.text = seasonal
            ? "PVE CAMPAIGN"
                + (
                    string.IsNullOrWhiteSpace(character.SeasonName)
                        ? ""
                        : "  •  " + WTT.Campaigns.Shared.Presentation.CampaignText.Display(character.SeasonName).ToUpperInvariant()
                )
            : "PVE ZONE  •  REGULAR";
        _switchLoader!.transform.SetAsLastSibling();
        _switchLoader.ShowGameObject(instant: true);
        _switchLoader.CanvasGroup.alpha = 1;
        _switchLoader.CanvasGroup.blocksRaycasts = true;
        _switchLoader._logoGroup.alpha = 1;
        _switchLoader._screenAnimator.Play("LoadingState", 0, 0);
        _switchLoader._screenAnimator.Update(0);

        using var frame = new CancellationTokenSource();
        _switchLoadingCancellation = frame;
        try
        {
            // Two distinct frames preserve the loading screen's chance to render before
            // saving/reconnecting performs synchronous main-thread work.
            await UniTask.NextFrame(frame.Token);
            await UniTask.NextFrame(frame.Token);
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
        if (_switchLoader)
        {
            _switchLoader!.Close();
        }
    }
}
