using UnityEngine;
using UnityEngine.UIElements;
using ZLinq;

namespace WTT.Campaigns.Client.Authoring.Views;

internal sealed partial class RaidEditorView
{
    private void ApplyToolWindowPresentation()
    {
        var specs = new Dictionary<string, EditorLayoutSpec.Node>();
        void Read(EditorLayoutSpec.Node node)
        {
            specs.Add(node.Id, node);
            foreach (var child in node.Children)
                Read(child);
        }
        foreach (var node in EditorLayoutSpec.Sections)
            Read(node);

        foreach (var controls in _toolControls.Values.AsValueEnumerable().Append(_controls).ToArray())
        foreach (var pair in controls)
        {
            var element = pair.Value.Element;
            if (!EditorToolWindowStyle.Contains(element) || !specs.TryGetValue(pair.Key, out var spec))
                continue;
            if (pair.Value is EditorChoice choice)
            {
                var compact = spec.Id is "SceneSource" or "SceneFilter" or "ZoneCreateScope";
                choice.AddFieldLabel(spec.Id == "ZoneCreateScope" ? "Scope" : spec.Text, compact);
            }
            var help = EditorToolHelp.For(spec.Id, spec.Text);
            if (pair.Value is EditorInput input)
            {
                element.tooltip =
                    help
                    + (
                        input.NumericDrag != null ? "\nDrag the label or Alt-drag the value. Shift: finer; Ctrl: faster; Escape: cancel."
                        : spec.Id == "EnvironmentHour" ? ""
                        : "\nPress Enter or leave the field to apply."
                    );
                if (spec.Id == "Search")
                    element.tooltip = "Filter this window by name or identifier. Clear the search to show all records.";
            }
            else if (pair.Value is EditorButton button)
            {
                button.Help = help;
                element.tooltip = help;
                if (spec.Id is "Delete" or "MapDelete" or "SceneRemove" or "ContainerRemove")
                    element.AddToClassList("editor-destructive");
            }
            else if (pair.Value is EditorChoice)
                element.tooltip = help;
        }

        foreach (var tool in ToolIds)
        {
            var controls = _toolControls[tool];
            if (tool == "Bindings")
            {
                foreach (
                    var (id, help) in new[]
                    {
                        ("AddBox", "Create a trigger event binding."),
                        ("AddSphere", "Create an interaction event binding."),
                    }
                )
                {
                    var button = (EditorButton)controls[id];
                    button.Help = button.Element.tooltip = help;
                }
            }
            var creation = controls["CreationTools"].Element;
            var title = creation.Q<Label>(className: "editor-creation-heading");
            title.text = tool switch
            {
                "Layouts" or "Zones" or "Hazards" => "CREATE",
                "Routes" => "MARKERS",
                "Scene" => "SCENE",
                "Captures" => "CAPTURE",
                "Bindings" => "EVENTS",
                _ => "",
            };
            title.style.display = tool == "Scene" ? DisplayStyle.None : DisplayStyle.Flex;
            EditorToolWindowStyle.Apply(controls["Library"].Element);
            if (tool == "Scene")
                SeparateSceneTools(controls);
            else if (tool is "Hazards" or "Zones")
            {
                var actions = Document.Clone<VisualElement>("ScopedActions");
                foreach (
                    var id in tool == "Hazards"
                        ? new[] { "AddMinefield", "AddClaymore", "AddSniper", "AddBarbedWire" }
                        : new[] { "AddBox", "AddSphere" }
                )
                    actions.Add(controls[id].Element);
                creation.Add(actions);
            }
        }

        foreach (var id in new[] { "Inspector", "EnvironmentMenu", "LootConfiguration", "Controls" })
            EditorToolWindowStyle.Apply(Element(id));
    }

    private void SeparateSceneTools(Dictionary<string, EditorControl> controls)
    {
        controls["Library"].Element.AddToClassList("editor-scene-tool");
        var creation = controls["CreationTools"].Element;
        var scroll = (ScrollView)controls["ToolActionsScroll"].Element;
        scroll.mode = ScrollViewMode.Horizontal;
        scroll.horizontalScrollerVisibility = ScrollerVisibility.Auto;
        scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
        foreach (
            var (name, caption, ids) in new[]
            {
                ("ScenePickActions", "SELECT", new[] { "Pick" }),
                ("ScenePropActions", "PROPS", new[] { "MapMoveObject", "MapCopyObject", "MapHideObject" }),
                ("SceneWorldActions", "WORLD", new[] { "MapBarrier", "MapDoor" }),
            }
        )
        {
            var row = Document.Clone<VisualElement>("SceneActionGroup");
            row.name = name;
            row.EnableInClassList("editor-scene-action-divider", name != "ScenePickActions");
            row.Q<Label>("Caption").text = caption;
            foreach (var id in ids)
            {
                var button = controls[id].Element;
                row.Add(button);
            }
            creation.Add(row);
        }
    }
}
