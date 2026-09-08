using UnityEngine;
using UnityEngine.UI;

namespace SeasonalPerks.UI.Controls;

public static class StoryTaskLayout
{
    public static RectTransform CreatePanel(RectTransform native)
    {
        var panel = UiElements.Rect("Seasonal story journal", native.parent, 0, 0);
        panel.SetSiblingIndex(native.GetSiblingIndex());
        panel.anchorMin = native.anchorMin;
        panel.anchorMax = native.anchorMax;
        panel.sizeDelta = native.sizeDelta;
        panel.pivot = new Vector2(.5f, .5f);
        panel.anchoredPosition = native.anchoredPosition + Vector2.Scale(panel.pivot - native.pivot, native.rect.size);
        panel.localScale = native.localScale;
        panel.localRotation = native.localRotation;
        // TasksPart controls its children's size. Without these layout inputs the
        // journal collapses to zero even if its RectTransform was copied correctly.
        var source = native.GetComponent<LayoutElement>();
        if (source)
        {
            var layout = panel.gameObject.AddComponent<LayoutElement>();
            layout.ignoreLayout = source.ignoreLayout;
            layout.minWidth = source.minWidth;
            layout.minHeight = source.minHeight;
            layout.preferredWidth = source.preferredWidth;
            layout.preferredHeight = source.preferredHeight;
            layout.flexibleWidth = source.flexibleWidth;
            layout.flexibleHeight = source.flexibleHeight;
            layout.layoutPriority = source.layoutPriority;
        }
        panel.gameObject.AddComponent<RectMask2D>();
        return panel;
    }
}
