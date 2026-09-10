using UnityEngine;
using UnityEngine.UI;
using WTT.Campaigns.Client.UI;
using WTT.Campaigns.UI.Controls;

namespace WTT.Campaigns.Client.Story;

internal sealed class StoryInteractionPrompt : IDisposable
{
    private readonly GameObject _root;

    internal StoryInteractionPrompt()
    {
        _root = new GameObject("Campaign story interaction", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        _root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        _root.GetComponent<Canvas>().sortingOrder = 31900;
        var scaler = _root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        var font = SeasonUi.Instance.UiBundle.LoadAsset<Font>("assets/mods/wtt-campaigns.assets/fonts/bender.ttf");
        var label = new UiElements(font).Label(_root.transform, "Interaction", "Interact", 20, 240, 38, 0, -105);
        label.alignment = TextAnchor.MiddleCenter;
        label.raycastTarget = false;
        label.color = new Color(.91f, .88f, .73f);
        _root.SetActive(false);
    }

    internal void Show(bool visible)
    {
        _root.SetActive(visible);
    }

    public void Dispose()
    {
        UnityEngine.Object.Destroy(_root);
    }
}
