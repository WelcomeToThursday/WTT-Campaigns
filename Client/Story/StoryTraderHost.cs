using EFT.UI;
using UnityEngine;
using UnityEngine.UI;
using WTT.Campaigns.Client.UI;
using WTT.Campaigns.UI.Controls;

namespace WTT.Campaigns.Client.Story;

public sealed class StoryTraderHost : MonoBehaviour
{
    private TraderScreensGroup? _native;
    private StoryTradeTabRow? _visit;
    private bool _loading;
    internal TraderScreensGroup Native
    {
        get { return _native!; }
    }

    internal void Attach(TraderScreensGroup native)
    {
        _native = native;
        if (!_visit)
        {
            var buy = (RectTransform)native._traderDealScreen._buyTab.transform;
            var sell = (RectTransform)native._traderDealScreen._sellTab.transform;
            var font = SeasonUi.Instance.UiBundle.LoadAsset<Font>("assets/mods/wtt-campaigns.assets/fonts/bender.ttf");
            _visit = StoryTradeTabRow.Create(
                buy,
                sell,
                font,
                () => native._traderDealScreen._buyTab.OnPointerClick(new UnityEngine.EventSystems.PointerEventData(null)),
                () => native._traderDealScreen._sellTab.OnPointerClick(new UnityEngine.EventSystems.PointerEventData(null)),
                Open,
                SeasonUi.Instance.PlayInterfaceSound
            );
        }
        _visit!.gameObject.SetActive(StoryClient.Available && StoryMediaStore.HasTrader(native.Trader.Id));
        if (StoryClient.Available && !_loading)
        {
            LoadStory();
        }
    }

    private async void LoadStory()
    {
        _loading = true;
        try
        {
            await StoryClient.Load();
        }
        catch (Exception exception)
        {
            Plugin.LogInfo("Trader story could not load: " + exception.Message);
        }
        finally
        {
            _loading = false;
        }
    }

    private void Update()
    {
        if (_native && _visit)
        {
            // Selecting a portrait reuses this screen without calling Show again.
            _visit!.gameObject.SetActive(StoryClient.Available && StoryMediaStore.HasTrader(_native!.Trader.Id));
            var deal = _native!._traderDealScreen;
            _visit.SetState(deal.TradeMode == ETradeMode.Purchase, deal._buyTab.Interactable, deal._sellTab.Interactable, !Plugin.Busy);
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
