using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SeasonalPerks.UI.Modifiers;

public sealed class PerkPointer : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
{
    public Action? Enter;
    public Action? Exit;

    public void OnPointerEnter(PointerEventData eventData) => Enter?.Invoke();

    public void OnPointerExit(PointerEventData eventData) => Exit?.Invoke();

    public void OnSelect(BaseEventData eventData) => Enter?.Invoke();

    public void OnDeselect(BaseEventData eventData) => Exit?.Invoke();

    private void OnDisable() => Exit?.Invoke();
}
