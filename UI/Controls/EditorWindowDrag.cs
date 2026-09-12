using UnityEngine;
using UnityEngine.EventSystems;

namespace WTT.Campaigns.UI.Controls;

// Attached at runtime: SDK bundles contain native uGUI components only.
public sealed class EditorWindowDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IPointerDownHandler
{
    public RectTransform Window = null!;
    public RectTransform Boundary = null!;
    public bool Movable = true;
    private Vector2 _pointer,
        _position;

    public void OnPointerDown(PointerEventData eventData)
    {
        if (Movable && eventData.button == PointerEventData.InputButton.Left)
            Window.SetAsLastSibling();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (!Movable || eventData.button != PointerEventData.InputButton.Left)
            return;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(Boundary, eventData.position, eventData.pressEventCamera, out _pointer);
        _position = Window.anchoredPosition;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!Movable || eventData.button != PointerEventData.InputButton.Left)
            return;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(Boundary, eventData.position, eventData.pressEventCamera, out var point);
        Window.anchoredPosition = _position + point - _pointer;
        Clamp();
    }

    public void Clamp()
    {
        if (!Movable)
            return;
        var half = Window.rect.size * .5f;
        var bounds = Boundary.rect;
        var position = Window.anchoredPosition;
        position.x = Mathf.Clamp(position.x, bounds.xMin + half.x, Mathf.Max(bounds.xMin + half.x, bounds.xMax - half.x));
        position.y = Mathf.Clamp(position.y, bounds.yMin + half.y, Mathf.Max(bounds.yMin + half.y, bounds.yMax - half.y));
        Window.anchoredPosition = position;
    }
}
