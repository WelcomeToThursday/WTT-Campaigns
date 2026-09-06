using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SeasonalPerks.UI.Modifiers;

public sealed class PerkCardHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public GameObject? Idle;
    public GameObject? Selected;
    public GameObject Highlight = null!;
    public Func<bool>? Allowed;
    public Action? Entered;
    private bool _hovered;
    private bool _selected;

    public void Refresh(bool selected)
    {
        _selected = selected;
        Apply();
    }

    public void ClearHover()
    {
        _hovered = false;
        Apply();
    }

    private void Apply()
    {
        var hover = _hovered && (Allowed?.Invoke() ?? true);
        if (Idle)
        {
            Idle!.SetActive(!_selected && !hover);
        }
        if (Selected)
        {
            Selected!.SetActive(_selected && !hover);
        }
        if (Highlight)
        {
            Highlight.SetActive(hover);
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        _hovered = Allowed?.Invoke() ?? true;
        Apply();
        if (_hovered)
        {
            Entered?.Invoke();
        }
    }

    public void OnPointerExit(PointerEventData eventData) => ClearHover();

    private void OnDisable() => ClearHover();
}
