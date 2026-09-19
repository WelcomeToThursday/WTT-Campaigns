using UnityEngine;
using UnityEngine.UIElements;

namespace WTT.Campaigns.Client.Authoring.Views;

// C# assigns semantic/state classes; Unity USS owns presentation.
internal static class EditorControlLayout
{
    internal const int TreeRowHeight = 24;

    internal static void Action(Button button) => button.AddToClassList("editor-action");

    internal static void Row(VisualElement row) => row.AddToClassList("editor-action-row");

    internal static void Field(TextField field, bool narrow)
    {
        field.AddToClassList("editor-field");
        field.EnableInClassList("editor-field-axis", field.parent?.ClassListContains("editor-axes") == true);
        field.EnableInClassList("editor-field-search", field.name == "Search");
        field.EnableInClassList("editor-field-narrow", narrow && !EditorToolWindowStyle.Contains(field));
    }

    internal static void ListRow(Button button) => button.AddToClassList("editor-record-row");

    internal static void TreeRow(VisualElement row, Foldout fold, Label label)
    {
        row.AddToClassList("editor-native-tree-row");
        fold.contentContainer.AddToClassList("editor-hidden");
        fold.Q<Toggle>().labelElement.AddToClassList("editor-hidden");
    }
}
