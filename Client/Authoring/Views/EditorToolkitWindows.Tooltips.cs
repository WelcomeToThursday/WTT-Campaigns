using UnityEngine;
using UnityEngine.UIElements;

namespace WTT.Campaigns.Client.Authoring.Views;

internal sealed partial class EditorToolkitWindows
{
    internal void SetTooltip(string id, string text)
    {
        _view.Element(id).tooltip = text;
        if (_view.Element(id) is Label)
            _view.Element(id).pickingMode = PickingMode.Position;
    }

    internal void ShowTooltip(string id, string fallback, VisualElement? anchor = null)
    {
        if (_walkthrough || _view.IsVisible("ConflictShield"))
            return;
        var text = fallback;
        if (string.IsNullOrWhiteSpace(text))
        {
            HideTooltip();
            return;
        }
        _tooltipAnchor = anchor ?? _view.Element(id);
        _tooltip.text = text;
        _tooltip.style.maxWidth = Math.Min(360, _view.Document.Width - 16);
        _tooltip.style.visibility = Visibility.Hidden;
        _tooltip.style.display = DisplayStyle.Flex;
        _tooltip.BringToFront();
    }

    private void PlaceTooltip()
    {
        if (_tooltip.style.display.value == DisplayStyle.None)
            return;
        if (_tooltipAnchor?.panel == null)
        {
            HideTooltip();
            return;
        }
        var size = _tooltip.layout.size;
        if (!float.IsFinite(size.x) || !float.IsFinite(size.y) || size.x <= 0 || size.y <= 0)
            return;
        // Element bounds and the tooltip parent share panel coordinates. Using
        // the measured width avoids reserving 360px for a short toolbar hint.
        var parent = _view.Document.Content;
        var min = parent.WorldToLocal(_tooltipAnchor.worldBound.min);
        var max = parent.WorldToLocal(_tooltipAnchor.worldBound.max);
        var position = EditorTooltipPlacement.Place(
            min.x,
            max.x,
            min.y,
            max.y,
            size.x,
            size.y,
            _view.Document.Width,
            _view.Document.Height
        );
        _tooltip.style.left = position.X;
        _tooltip.style.top = position.Y;
        _tooltip.style.visibility = Visibility.Visible;
    }

    internal void HideTooltip()
    {
        _tooltipAnchor = null;
        _tooltip.style.display = DisplayStyle.None;
    }
}
