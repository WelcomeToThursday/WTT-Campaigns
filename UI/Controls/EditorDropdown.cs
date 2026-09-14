using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace WTT.Campaigns.UI.Controls;

// Keep the popup outside scrolling properties, and route its input before editor shortcuts.
public sealed class EditorDropdown : Dropdown
{
    private GameObject? _popup;
    private int _dismissedFrame = -1;
    private readonly Vector3[] _corners = new Vector3[4];
    public bool IsOpen => _popup && _popup!.activeSelf || _dismissedFrame == Time.frameCount;

    private void PlaceTemplate()
    {
        ((RectTransform)transform).GetWorldCorners(_corners);
        template.position = _corners[0];
        template.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,
            Vector3.Distance(_corners[0], _corners[3]) / template.lossyScale.x);
    }

    public override void OnPointerClick(PointerEventData eventData)
    {
        PlaceTemplate();
        base.OnPointerClick(eventData);
    }

    public override void OnSubmit(BaseEventData eventData)
    {
        PlaceTemplate();
        base.OnSubmit(eventData);
    }

    protected override GameObject CreateDropdownList(GameObject templateObject)
    {
        _popup = base.CreateDropdownList(templateObject);
        return _popup;
    }

    protected override void DestroyDropdownList(GameObject dropdownList)
    {
        _popup = null;
        base.DestroyDropdownList(dropdownList);
    }

    public override void OnCancel(BaseEventData eventData)
    {
        _dismissedFrame = Time.frameCount;
        base.OnCancel(eventData);
    }

    public void Dismiss()
    {
        Hide();
        _dismissedFrame = Time.frameCount;
    }
}
