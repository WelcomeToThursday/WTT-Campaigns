using EFT.UI;
using SeasonalPerks.Client.UI;
using SeasonalPerks.UI.Controls;
using UnityEngine;
using UnityEngine.UI;

namespace SeasonalPerks.Client.Story;

public sealed class StoryTraderHost : MonoBehaviour
{
    private TraderScreensGroup? _native;
    private Button? _visit;
    internal TraderScreensGroup Native
    {
        get { return _native!; }
    }

    internal void Attach(TraderScreensGroup native)
    {
        _native = native;
        if (!_visit)
        {
            var tab = (RectTransform)native._servicesTab.transform;
            var font = SeasonUi.Instance.UiBundle.LoadAsset<Font>("assets/mods/seasonalperks.assets/fonts/bender.ttf");
            _visit = StoryVisitButton.Create(tab.parent.parent, font, Open, SeasonUi.Instance.PlayInterfaceSound);
        }
        _visit!.gameObject.SetActive(StoryClient.Available && StoryMediaStore.HasTrader(native.Trader.Id));
    }

    private void Update()
    {
        if (_native && _visit)
        {
            // Selecting a portrait reuses this screen without calling Show again.
            _visit!.gameObject.SetActive(StoryClient.Available && StoryMediaStore.HasTrader(_native!.Trader.Id));
        }
    }

    private void Open()
    {
        if (_native && StoryClient.Available && !Plugin.Busy)
        {
            StoryVisitRuntime.Instance.Open(this);
        }
    }

    private void OnDisable()
    {
        if (_visit)
        {
            _visit!.gameObject.SetActive(false);
        }
        if (StoryVisitRuntime.Instance)
        {
            StoryVisitRuntime.Instance.Detach(this);
        }
    }
}
