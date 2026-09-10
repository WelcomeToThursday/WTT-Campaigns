using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace WTT.Campaigns.UI.BattlePass;

public sealed class HubPointer : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IScrollHandler
{
    public Action<bool>? Hover;
    public Action<int>? Scroll;

    public void OnPointerEnter(PointerEventData eventData)
    {
        Hover?.Invoke(true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        Hover?.Invoke(false);
    }

    public void OnScroll(PointerEventData eventData)
    {
        if (Math.Abs(eventData.scrollDelta.y) > .01f)
        {
            Scroll?.Invoke(eventData.scrollDelta.y > 0 ? -1 : 1);
        }
    }

    private void OnDisable()
    {
        Hover?.Invoke(false);
    }
}
