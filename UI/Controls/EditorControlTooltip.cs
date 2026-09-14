using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace WTT.Campaigns.UI.Controls;

// Runtime-only pointer behavior; no custom scripts are serialized into the UI bundle.
public sealed class EditorControlTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler
{
    public Action? Enter,
        Exit;

    private bool _pending;
    private float _showAt;

    public void OnPointerEnter(PointerEventData data)
    {
        _pending = true;
        _showAt = Time.unscaledTime + .45f;
    }

    private void Update()
    {
        if (!_pending || Time.unscaledTime < _showAt) return;
        _pending = false;
        Enter?.Invoke();
    }

    public void OnPointerExit(PointerEventData data) => Hide();

    public void OnPointerDown(PointerEventData data) => Hide();

    private void OnDisable() => Hide();

    private void Hide()
    {
        _pending = false;
        Exit?.Invoke();
    }
}
