using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WTT.Campaigns.UI.Audio;

namespace WTT.Campaigns.UI.Controls;

public sealed class UiButtonFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private Button? _button;
    private Action<InterfaceSound>? _play;
    private InterfaceSound _click;
    private Func<bool>? _allowed;
    private bool _hovered;

    public void Initialize(Button button, Action<InterfaceSound> play, InterfaceSound click, Func<bool>? allowed)
    {
        if (_button)
        {
            _button!.onClick.RemoveListener(Click);
        }
        _button = button;
        _play = play;
        _click = click;
        _allowed = allowed;
        button.onClick.AddListener(Click);
    }

    private bool CanPlay
    {
        get { return _button && _button!.IsActive() && _button.IsInteractable() && (_allowed?.Invoke() ?? true); }
    }

    private void Click()
    {
        if (CanPlay && _click != InterfaceSound.None)
        {
            _play?.Invoke(_click);
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_hovered)
        {
            return;
        }
        _hovered = true;
        if (CanPlay)
        {
            _play?.Invoke(InterfaceSound.ButtonHover);
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _hovered = false;
    }

    private void OnDisable()
    {
        _hovered = false;
    }
}
