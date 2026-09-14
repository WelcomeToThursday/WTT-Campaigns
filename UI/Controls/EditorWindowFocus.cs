using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace WTT.Campaigns.UI.Controls;

public sealed class EditorWindowFocus : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public Action? Focus;
    private bool _over;
    public void OnPointerEnter(PointerEventData e) => _over = true;
    public void OnPointerExit(PointerEventData e) => _over = false;
    private void Update()
    {
        if (_over && Input.GetMouseButtonDown(0)) Focus?.Invoke();
    }
    private void OnDisable() => _over = false;
}
