using UnityEngine;
using UnityEngine.UIElements;

namespace WTT.Campaigns.Client.Authoring.Views;

// Scoped runtime styling keeps the shipped theme and the fixed toolbar geometry intact.
internal static class EditorToolWindowStyle
{
    internal static bool Contains(VisualElement element)
    {
        for (var parent = element.parent; parent != null; parent = parent.parent)
            if (parent.ClassListContains("editor-window"))
                return true;
        return false;
    }

    internal static void Heading(Label label)
    {
        label.style.fontSize = 12;
        label.style.unityFontStyleAndWeight = FontStyle.Bold;
        label.style.color = new Color(.72f, .69f, .54f);
        label.style.marginTop = 6;
        label.style.marginBottom = 4;
        label.style.whiteSpace = WhiteSpace.Normal;
        label.style.flexShrink = 0;
    }

    internal static void Apply(VisualElement window)
    {
        window.style.fontSize = 14;
        window.style.paddingLeft = window.style.paddingRight = 8;
        foreach (var title in window.Query<VisualElement>(className: "editor-window-title").ToList())
        {
            title.style.height = 28;
            title.style.marginBottom = 2;
        }
        foreach (var group in window.Query<VisualElement>(className: "editor-group").ToList())
        {
            group.style.marginLeft = group.style.marginRight = 0;
            group.style.marginTop = 0;
            group.style.marginBottom = 5;
        }
        foreach (var row in window.Query<VisualElement>(className: "editor-actions").ToList())
        {
            row.style.marginLeft = row.style.marginRight = 0;
            row.style.marginTop = row.style.marginBottom = 2;
            row.style.paddingTop = 0;
            row.style.alignItems = Align.Stretch;
        }
        foreach (var button in window.Query<Button>().ToList())
        {
            if (button.ClassListContains("editor-browser-row") || button.ClassListContains("editor-icon-button"))
                continue;
            button.style.minHeight = 26;
            button.style.marginLeft = 0;
            button.style.marginRight = 4;
            button.style.marginTop = button.style.marginBottom = 2;
            button.style.paddingTop = button.style.paddingBottom = 3;
            button.style.paddingLeft = button.style.paddingRight = 8;
            button.style.whiteSpace = WhiteSpace.Normal;
            button.style.alignSelf = Align.FlexStart;
            button.style.flexGrow = 0;
            button.style.flexBasis = StyleKeyword.Auto;
            button.style.minWidth = 0;
        }
        foreach (var field in window.Query<TextField>().ToList())
        {
            EditorControlLayout.Field(field, false);
            field.style.minWidth = 0;
            if (field.parent?.ClassListContains("editor-axes") != true && field.name != "Search")
            {
                field.style.width = Length.Percent(100);
                field.style.maxWidth = 420;
            }
            field.style.minHeight = 26;
            field.style.marginTop = field.style.marginBottom = 2;
            field.labelElement.style.color = new Color(.64f, .67f, .64f);
            var input = field.Q(className: "unity-base-text-field__input");
            if (input != null)
                input.style.paddingTop = input.style.paddingBottom = 3;
        }
        foreach (var axes in window.Query<VisualElement>(className: "editor-axes").ToList())
            axes.style.maxWidth = 480;
        foreach (var label in window.Query<Label>(className: "editor-label").ToList())
        {
            label.style.marginTop = label.style.marginBottom = 2;
            if (label.name.EndsWith("Heading") || label.name.EndsWith("Caption") || label.name == "EnvironmentTitle")
                Heading(label);
        }
        foreach (var section in window.Query<VisualElement>().ToList())
            if (section.name.EndsWith("Section"))
            {
                section.style.marginTop = 5;
                section.style.marginBottom = 6;
                section.style.paddingTop = 4;
                section.style.paddingBottom = 4;
                section.style.borderTopWidth = 1;
                section.style.borderTopColor = new Color(.25f, .27f, .25f);
                section.style.flexShrink = 0;
            }

        // Apply compact strips last: generic action/field styling must not change
        // their control heights or turn the paging pair into a wrapping column.
        foreach (var id in new[] { "BrowserFilters", "SceneTabs", "SceneFilters", "CatalogViews", "CreationTools", "Paging" })
        {
            var row = window.Q<VisualElement>(id);
            if (row == null)
                continue;
            row.style.alignItems = Align.Center;
            row.style.marginTop = row.style.marginBottom = 0;
            foreach (var child in row.Children())
            {
                child.style.alignSelf = Align.Center;
                if (child is not Button && child is not TextField)
                    continue;
                child.style.height = child.style.minHeight = child.style.maxHeight = 30;
                child.style.marginTop = child.style.marginBottom = 2;
                child.style.paddingTop = child.style.paddingBottom = 0;
                child.style.whiteSpace = WhiteSpace.NoWrap;
                child.style.unityTextAlign = TextAnchor.MiddleCenter;
                foreach (var label in child.Query<Label>().ToList())
                {
                    label.style.marginTop = label.style.marginBottom = 0;
                    label.style.paddingTop = label.style.paddingBottom = 0;
                    label.style.alignSelf = Align.Center;
                    label.style.whiteSpace = WhiteSpace.NoWrap;
                    label.style.unityTextAlign = TextAnchor.MiddleLeft;
                    label.style.overflow = Overflow.Hidden;
                    label.style.textOverflow = TextOverflow.Ellipsis;
                }
            }
        }
        var footer = window.Q<VisualElement>("BrowserFooter");
        if (footer != null)
        {
            footer.style.flexWrap = Wrap.NoWrap;
            footer.style.alignItems = Align.Center;
            var count = footer.Q<Label>("LibraryCount");
            count.style.flexBasis = 0;
            count.style.flexShrink = 1;
            count.style.minWidth = 0;
            count.style.whiteSpace = WhiteSpace.NoWrap;
            count.style.overflow = Overflow.Hidden;
            count.style.textOverflow = TextOverflow.Ellipsis;
            var paging = footer.Q<VisualElement>("Paging");
            paging.style.flexDirection = FlexDirection.Row;
            paging.style.flexWrap = Wrap.NoWrap;
            paging.style.flexShrink = 0;
            paging.style.maxWidth = StyleKeyword.None;
            foreach (var button in paging.Query<Button>().ToList())
            {
                button.style.flexGrow = button.style.flexShrink = 0;
                button.style.maxWidth = StyleKeyword.None;
            }
        }
    }

    internal static void Divider(VisualElement group)
    {
        group.style.borderTopWidth = 1;
        group.style.borderTopColor = new Color(.30f, .32f, .28f);
        group.style.marginTop = 6;
        group.style.paddingTop = 6;
    }
}
