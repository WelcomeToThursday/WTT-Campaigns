using UnityEngine.UIElements;

namespace WTT.Campaigns.Client.Authoring.Views;

internal static class EditorScrollStyle
{
    internal static void Apply(ScrollView scroll)
    {
        scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
        scroll.verticalScroller.AddToClassList("editor-scrollbar");
        scroll.verticalScroller.lowButton.AddToClassList("editor-hidden");
        scroll.verticalScroller.highButton.AddToClassList("editor-hidden");
        scroll.verticalScroller.slider.AddToClassList("editor-scroll-slider");
    }
}
