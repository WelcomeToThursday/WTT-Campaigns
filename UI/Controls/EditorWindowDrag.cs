using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace WTT.Campaigns.UI.Controls;

// Attached at runtime: SDK bundles contain native uGUI components only.
public sealed class EditorWindowDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IPointerDownHandler, IEndDragHandler
{
    public RectTransform Window = null!;
    public RectTransform Boundary = null!;
    public bool Movable = true;
    public float BottomInset = 40;
    public bool Dragging { get; private set; }
    public Action? Completed, Focus;
    private Vector2 _pointer,
        _position;

    public void OnPointerDown(PointerEventData eventData)
    {
        if (Movable && eventData.button == PointerEventData.InputButton.Left)
            Focus?.Invoke();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (!Movable || eventData.button != PointerEventData.InputButton.Left)
            return;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(Boundary, eventData.position, eventData.pressEventCamera, out _pointer);
        _position = Window.anchoredPosition;
        Dragging = true;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!Movable || eventData.button != PointerEventData.InputButton.Left)
            return;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(Boundary, eventData.position, eventData.pressEventCamera, out var point);
        Window.anchoredPosition = _position + point - _pointer;
        Clamp();
    }

    public void OnEndDrag(PointerEventData e)
    {
        if (!Dragging) return;
        Dragging = false;
        Completed?.Invoke();
    }

    private void OnDisable() => Dragging = false;

    public void Clamp()
    {
        if (!Movable)
            return;
        var half = Window.rect.size * .5f;
        var bounds = Boundary.rect;
        var position = Window.anchoredPosition;
        position.x = Mathf.Clamp(position.x, bounds.xMin + half.x + 8, Mathf.Max(bounds.xMin + half.x + 8, bounds.xMax - half.x - 8));
        position.y = Mathf.Clamp(position.y, bounds.yMin + half.y + BottomInset, Mathf.Max(bounds.yMin + half.y + BottomInset, bounds.yMax - half.y - 92));
        Window.anchoredPosition = position;
    }
}
