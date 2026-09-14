using UnityEngine;
using UnityEngine.EventSystems;

namespace WTT.Campaigns.UI.Controls;

// A click belongs to the record displayed at pointer-down, even if indexing finishes meanwhile.
public sealed class EditorRowSelection : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    public string Identity = "";
    private string? _pressed;
    public bool Pressed { get; private set; }

    public string Consume()
    {
        var id = _pressed ?? Identity;
        _pressed = null;
        return id;
    }

    public void OnPointerDown(PointerEventData data)
    {
        if (data.button != PointerEventData.InputButton.Left)
            return;
        _pressed = Identity;
        Pressed = true;
    }

    public void OnPointerUp(PointerEventData data)
    {
        Pressed = false;
    }

    private void OnDisable()
    {
        Pressed = false;
        _pressed = null;
    }
}
