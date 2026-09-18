using UnityEngine.UIElements;

namespace WTT.Campaigns.Client.Authoring.Views;

// Assign semantic classes; shared geometry and appearance live in Editor.uss.
internal static class EditorToolWindowStyle
{
    internal static bool Contains(VisualElement element)
    {
        for (var parent = element.parent; parent != null; parent = parent.parent)
            if (parent.ClassListContains("editor-window"))
                return true;
        return false;
    }

    internal static void Heading(Label label) => label.AddToClassList("editor-heading");

    internal static void Apply(VisualElement window)
    {
        window.AddToClassList("editor-tool-window");
        foreach (var field in window.Query<TextField>().ToList())
            EditorControlLayout.Field(field, false);
        foreach (var label in window.Query<Label>(className: "editor-label").ToList())
            if (label.name.EndsWith("Heading") || label.name.EndsWith("Caption") || label.name == "EnvironmentTitle")
                Heading(label);
        foreach (var section in window.Query<VisualElement>().ToList())
            if (section.name.EndsWith("Section"))
                section.AddToClassList("editor-section");
        foreach (var id in new[] { "BrowserFilters", "SceneTabs", "SceneFilters", "CatalogViews", "CreationTools", "Paging" })
            window.Q<VisualElement>(id)?.AddToClassList("editor-compact-strip");
    }

    internal static void Divider(VisualElement group) => group.AddToClassList("editor-divider");
}
