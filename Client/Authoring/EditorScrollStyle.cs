using UnityEngine;
using UnityEngine.UIElements;

namespace WTT.Campaigns.Client.Authoring;

// Shared by browser, property sheets and choice menus, including ListView's
// internal ScrollView. Inline styles override the native blue focus treatment.
internal static class EditorScrollStyle
{
    internal static void Apply(ScrollView scroll)
    {
        scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
        var bar = scroll.verticalScroller;
        bar.style.width = 10;
        bar.style.minWidth = 10;
        bar.style.marginLeft = 4;
        bar.style.backgroundColor = (Color)new Color32(32, 33, 32, 255);
        bar.lowButton.style.display = DisplayStyle.None;
        bar.highButton.style.display = DisplayStyle.None;
        var slider = bar.slider;
        slider.style.marginTop = slider.style.marginBottom = 0;
        slider.style.marginLeft = slider.style.marginRight = 0;
        var handle = slider.Q("unity-dragger");
        if (handle == null)
            return;
        handle.style.backgroundImage = StyleKeyword.None;
        handle.style.backgroundColor = (Color)new Color32(97, 99, 92, 255);
        handle.style.borderTopWidth = handle.style.borderBottomWidth = 0;
        handle.style.borderLeftWidth = handle.style.borderRightWidth = 0;
        handle.style.borderTopLeftRadius = handle.style.borderTopRightRadius = 0;
        handle.style.borderBottomLeftRadius = handle.style.borderBottomRightRadius = 0;
        handle.RegisterCallback<PointerEnterEvent>(_ => handle.style.backgroundColor = (Color)new Color32(167, 153, 104, 255));
        handle.RegisterCallback<PointerLeaveEvent>(_ => handle.style.backgroundColor = (Color)new Color32(97, 99, 92, 255));
        var border = slider.Q("unity-dragger-border");
        if (border != null)
            border.style.display = DisplayStyle.None;
        var track = slider.Q("unity-tracker");
        if (track != null)
        {
            track.style.backgroundImage = StyleKeyword.None;
            track.style.backgroundColor = (Color)new Color32(32, 33, 32, 255);
            track.style.borderTopWidth = track.style.borderBottomWidth = 0;
            track.style.borderLeftWidth = track.style.borderRightWidth = 0;
        }
    }
}
