using UnityEngine;
using UnityEngine.UIElements;

namespace WTT.Campaigns.Client.Authoring.Views;

// Runtime layout rules also override older installed Toolkit theme assets.
internal static class EditorControlLayout
{
    internal const int TreeRowHeight = 24;

    internal static void Action(Button button)
    {
        button.style.flexGrow = 0;
        button.style.flexShrink = 0;
        button.style.alignSelf = Align.FlexStart;
        button.style.maxWidth = Length.Percent(100);
        button.style.minHeight = 26;
        button.style.marginTop = button.style.marginBottom = 2;
        button.style.paddingTop = button.style.paddingBottom = 3;
        button.style.whiteSpace = WhiteSpace.Normal;
    }

    internal static void Row(VisualElement row)
    {
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.flexWrap = Wrap.Wrap;
        row.style.flexShrink = 0;
        row.style.minWidth = 0;
        row.style.maxWidth = Length.Percent(100);
    }

    internal static void Field(TextField field, bool narrow)
    {
        if (field.parent?.ClassListContains("editor-axes") == true)
        {
            field.style.flexDirection = FlexDirection.Row;
            field.style.flexGrow = field.style.flexShrink = 1;
            field.style.flexBasis = 0;
            field.style.minWidth = 0;
            field.labelElement.style.width = field.labelElement.style.minWidth = field.labelElement.style.maxWidth = 14;
            field.labelElement.style.flexShrink = 0;
            return;
        }
        if (field.name == "Search")
        {
            field.style.flexDirection = FlexDirection.Row;
            field.style.alignItems = Align.Center;
            field.style.alignSelf = Align.Center;
            field.style.width = 240;
            field.style.flexGrow = 0;
            field.style.flexShrink = 1;
            field.style.marginLeft = 0;
            field.style.maxWidth = 280;
            field.style.minHeight = 26;
            field.labelElement.style.width = field.labelElement.style.minWidth = 68;
            field.labelElement.style.maxWidth = 68;
            field.labelElement.style.whiteSpace = WhiteSpace.NoWrap;
            field.labelElement.style.paddingLeft = field.labelElement.style.paddingRight = 0;
            field.labelElement.style.marginLeft = 0;
            field.labelElement.style.flexShrink = 0;
            return;
        }
        // Tool windows have a bounded minimum width; keep a compact label/value column.
        if (EditorToolWindowStyle.Contains(field))
            narrow = false;
        field.style.flexDirection = narrow ? FlexDirection.Column : FlexDirection.Row;
        field.labelElement.style.width = narrow ? Length.Percent(100) : Length.Percent(35);
        field.labelElement.style.minWidth = narrow ? 0 : 70;
        field.labelElement.style.maxWidth = narrow ? new StyleLength(StyleKeyword.None) : new StyleLength(140);
    }

    internal static void ListRow(Button button)
    {
        // Full-width selection is useful for records, but should not look like an action button.
        button.style.flexGrow = 0;
        button.style.alignSelf = Align.Stretch;
        button.style.borderLeftWidth = button.style.borderRightWidth = button.style.borderTopWidth = button.style.borderBottomWidth = 0;
        button.style.marginLeft = button.style.marginRight = 0;
        button.style.marginTop = button.style.marginBottom = 0;
    }

    private static void ClearSpacing(VisualElement element)
    {
        element.style.marginLeft = element.style.marginRight = element.style.marginTop = element.style.marginBottom = 0;
        element.style.paddingLeft = element.style.paddingRight = element.style.paddingTop = element.style.paddingBottom = 0;
    }

    internal static void TreeRow(VisualElement row, Foldout fold, Label label)
    {
        ClearSpacing(row);
        row.style.height = row.style.minHeight = row.style.maxHeight = TreeRowHeight;
        row.style.flexShrink = 0;
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.overflow = Overflow.Hidden;
        ClearSpacing(label);
        label.style.height = TreeRowHeight;
        label.style.minHeight = 0;
        label.style.minWidth = 0;
        label.style.flexGrow = label.style.flexShrink = 1;
        label.style.fontSize = 14;
        label.style.unityTextAlign = TextAnchor.MiddleLeft;
        label.style.whiteSpace = WhiteSpace.NoWrap;
        label.style.textOverflow = TextOverflow.Ellipsis;
        label.pickingMode = PickingMode.Ignore;
        // A Foldout contains a base field and a content container. Neither may
        // contribute the theme's 30px field minimum to a fixed-height list row.
        fold.contentContainer.style.display = DisplayStyle.None;
        var toggle = fold.Q<Toggle>();
        foreach (var element in new[] { (VisualElement)fold, toggle, toggle.Q(className: "unity-toggle__input") })
        {
            if (element == null)
                continue;
            ClearSpacing(element);
            element.style.width = element.style.minWidth = element.style.maxWidth = 18;
            element.style.height = element.style.minHeight = element.style.maxHeight = 18;
            element.style.flexGrow = element.style.flexShrink = 0;
            element.style.alignSelf = Align.Center;
            element.style.alignItems = Align.Center;
            element.style.justifyContent = Justify.Center;
        }
        toggle.labelElement.style.display = DisplayStyle.None;
        fold.style.marginRight = 4;
    }
}
