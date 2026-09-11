using System;
using UnityEngine;
using UnityEngine.UI;
using WTT.Campaigns.UI.Audio;

namespace WTT.Campaigns.UI.Controls;

[ExecuteAlways]
public sealed class StoryTradeTabRow : MonoBehaviour
{
    private RectTransform _buy = null!;
    private RectTransform _sell = null!;
    private bool _buyActive;
    private bool _sellActive;
    private StoryVisitButton _buyButton = null!;
    private StoryVisitButton _sellButton = null!;
    private StoryVisitButton _visitButton = null!;
    private StoryVisitButton[] _buttons = Array.Empty<StoryVisitButton>();
    private readonly Vector3[] _corners = new Vector3[4];

    public static StoryTradeTabRow Create(
        RectTransform buy,
        RectTransform sell,
        Font font,
        Action purchase,
        Action sale,
        Action visit,
        Action<InterfaceSound> sound
    )
    {
        var root = UiElements.Rect("Campaign trade tabs", buy.parent, 0, 0);
        root.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        var row = root.gameObject.AddComponent<StoryTradeTabRow>();
        row._buy = buy;
        row._sell = sell;
        row._buyActive = buy.gameObject.activeSelf;
        row._sellActive = sell.gameObject.activeSelf;
        row._buyButton = StoryVisitButton.CreateTab(root, font, "BUY", purchase, sound);
        row._sellButton = StoryVisitButton.CreateTab(root, font, "SELL", sale, sound);
        row._visitButton = StoryVisitButton.CreateTab(root, font, "VISIT", visit, sound);
        row._buttons = new[] { row._buyButton, row._sellButton, row._visitButton };
        row.RefreshLayout();
        return row;
    }

    public void SetState(bool purchase, bool canBuy, bool canSell, bool canVisit)
    {
        _buyButton.SetSelected(purchase);
        _sellButton.SetSelected(!purchase);
        _buyButton.interactable = canBuy;
        _sellButton.interactable = canSell;
        _visitButton.interactable = canVisit;
    }

    private void LateUpdate() => RefreshLayout();

    public void RefreshLayout()
    {
        if (!_buy || !_sell || !isActiveAndEnabled)
            return;
        // Keep the native geometry intact. Its fixed-size visual children cannot
        // be squeezed into smaller containers without overlapping icons/text.
        var parent = (RectTransform)transform.parent;
        _buy.GetWorldCorners(_corners);
        var left = parent.InverseTransformPoint(_corners[0]);
        var top = parent.InverseTransformPoint(_corners[1]);
        _sell.GetWorldCorners(_corners);
        var right = parent.InverseTransformPoint(_corners[3]);
        var width = right.x - left.x;
        var height = top.y - left.y;
        if (width <= 0 || height <= 0)
            return;
        var root = (RectTransform)transform;
        root.anchorMin = root.anchorMax = Vector2.zero;
        root.pivot = Vector2.zero;
        root.anchoredPosition = new Vector2(left.x - parent.rect.xMin, left.y - parent.rect.yMin);
        root.sizeDelta = new Vector2(width, height);
        StoryVisitButton.LayoutTabs(_buttons, width, height);
        _buy.gameObject.SetActive(false);
        _sell.gameObject.SetActive(false);
    }

    private void OnDisable()
    {
        if (_buy)
            _buy.gameObject.SetActive(_buyActive);
        if (_sell)
            _sell.gameObject.SetActive(_sellActive);
    }
}
