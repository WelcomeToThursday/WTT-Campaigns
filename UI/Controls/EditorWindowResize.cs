using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace WTT.Campaigns.UI.Controls;

public sealed class EditorWindowResize : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public RectTransform Window = null!, Boundary = null!;
    public Vector2 Minimum = new Vector2(360, 320);
    public float BottomInset = 40;
    public Action? Changed, Completed;
    private Vector2 _pointer, _size, _position;
    public bool Dragging { get; private set; }

    public void OnBeginDrag(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Left) return;
        Dragging = true;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(Boundary, e.position, e.pressEventCamera, out _pointer);
        _size = Window.sizeDelta;
        _position = Window.anchoredPosition;
    }

    public void OnDrag(PointerEventData e)
    {
        if (!Dragging) return;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(Boundary, e.position, e.pressEventCamera, out var point);
        var delta = point - _pointer;
        var size = new Vector2(
            Mathf.Clamp(_size.x + delta.x, Mathf.Min(Minimum.x, Boundary.rect.width - 16), Boundary.rect.xMax - (_position.x - _size.x / 2) - 8),
            Mathf.Clamp(_size.y - delta.y, Mathf.Min(Minimum.y, Boundary.rect.height - 92 - BottomInset), _position.y + _size.y / 2 - Boundary.rect.yMin - BottomInset));
        Window.sizeDelta = size;
        Window.anchoredPosition = _position + new Vector2((size.x - _size.x) / 2, -(size.y - _size.y) / 2);
        Changed?.Invoke();
    }

    public void OnEndDrag(PointerEventData e)
    {
        if (!Dragging) return;
        Dragging = false;
        Completed?.Invoke();
    }

    private void OnDisable() => Dragging = false;
}
