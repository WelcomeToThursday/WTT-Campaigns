using Cysharp.Threading.Tasks;
using EFT.Hideout;
using EFT.UI;
using UnityEngine;
using UnityEngine.UI;
using WTT.Campaigns.UI.Controls;

namespace WTT.Campaigns.Client.UI;

internal sealed class CampaignLoadingScreen : IDisposable
{
    private readonly GameObject _root;
    private readonly PveGameModeLoadingScreen _screen;
    private readonly Text _caption,
        _name,
        _detail;

    internal CampaignLoadingScreen(Font font)
    {
        _root = new GameObject(
            "Campaign loading screen",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster)
        );
        var canvas = _root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32750;
        var scaler = _root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = .5f;
        // Keep the native screen above our cards and independent of the backend's
        // own show/fade calls, so it stays visible until the profile is verified.
        _screen = UnityEngine.Object.Instantiate(PreloaderUI.Instance._pveLoadingScreen, _root.transform, false);
        _screen.name = "CampaignLoadingScreen";
        UiElements.Stretch((RectTransform)_screen.transform);

        var textRoot = UiElements.Rect("LoadingText", _screen.transform, 0, 0);
        UiElements.Stretch(textRoot);
        var textGroup = textRoot.gameObject.AddComponent<CanvasGroup>();
        textGroup.blocksRaycasts = false;
        textRoot.gameObject.AddComponent<LoadingTextAnimation>().Initialize(_screen._screenAnimator, textGroup);
        var ui = new UiElements(font);
        _caption = ui.Label(textRoot, "LoadingCaption", "LOADING CHARACTER", 14, 760, 24, 0, -130);
        _caption.alignment = TextAnchor.MiddleCenter;
        _caption.color = new Color(.35f, .69f, .75f, .8f);
        _name = ui.Label(textRoot, "LoadingCharacterName", "", 32, 760, 48, 0, -168);
        _name.alignment = TextAnchor.MiddleCenter;
        _name.color = new Color(.72f, .77f, .78f);
        _name.resizeTextForBestFit = true;
        _name.resizeTextMinSize = 18;
        _name.resizeTextMaxSize = 32;
        _detail = ui.Label(textRoot, "LoadingCharacterMode", "", 18, 900, 54, 0, -215);
        _detail.alignment = TextAnchor.MiddleCenter;
        _detail.color = new Color(.35f, .69f, .75f);
        _detail.resizeTextForBestFit = true;
        _detail.resizeTextMinSize = 14;
        _detail.resizeTextMaxSize = 18;
    }

    internal async UniTask Show(string caption, string name, string detail, CancellationToken token)
    {
        _caption.text = caption;
        _name.text = name;
        _detail.text = detail;
        _screen.ShowGameObject(instant: true);
        _screen.CanvasGroup.alpha = 1;
        _screen.CanvasGroup.blocksRaycasts = true;
        _screen._logoGroup.alpha = 1;
        _screen._screenAnimator.Play("LoadingState", 0, 0);
        _screen._screenAnimator.Update(0);
        // Give the shared loader two render frames before main-thread preparation.
        await UniTask.NextFrame(token);
        await UniTask.NextFrame(token);
    }

    public void Dispose()
    {
        if (_root)
        {
            _root.SetActive(false);
            UnityEngine.Object.Destroy(_root);
        }
    }
}
