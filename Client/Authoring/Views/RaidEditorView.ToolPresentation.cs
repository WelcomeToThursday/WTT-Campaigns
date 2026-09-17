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
                var compact = spec.Id is "SceneSource" or "ZoneCreateScope";
                choice.AddFieldLabel(spec.Id == "ZoneCreateScope" ? "Scope" : spec.Text, compact);
                element.style.minWidth = 0;
                element.style.width = compact ? 200 : 320;
                element.style.maxWidth = Length.Percent(100);
                element.style.alignSelf = Align.FlexStart;
                element.style.flexGrow = 0;
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
                    element.style.color = new Color(.94f, .64f, .60f);
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
            // Intrinsic groups share a row on wide windows and wrap only when needed.
            foreach (var id in new[] { "SceneTabs", "SceneFilters" })
            {
                var filters = controls[id].Element;
                filters.AddToClassList("editor-actions");
                filters.style.flexWrap = Wrap.Wrap;
                filters.style.flexShrink = 1;
                filters.style.width = StyleKeyword.Auto;
                filters.style.maxWidth = Length.Percent(100);
                filters.style.marginLeft = 0;
                filters.style.marginRight = 8;
            }
            controls["CatalogViews"].Element.style.marginLeft = 0;
            var creation = controls["CreationTools"].Element;
            creation.style.flexDirection = FlexDirection.Row;
            creation.style.alignItems = Align.Center;
            creation.AddToClassList("editor-actions");
            creation.RemoveFromClassList("editor-grid");
            var title = new Label(
                tool switch
                {
                    "Layouts" or "Zones" or "Hazards" => "CREATE",
                    "Routes" => "MARKERS",
                    "Scene" => "SCENE",
                    "Captures" => "CAPTURE",
                    "Bindings" => "EVENTS",
                    _ => "",
                }
            )
            {
                pickingMode = PickingMode.Ignore,
            };
            EditorToolWindowStyle.Heading(title);
            title.style.marginTop = title.style.marginBottom = 0;
            title.style.marginRight = 8;
            title.style.alignSelf = Align.Center;
            if (tool == "Scene")
                title.style.display = DisplayStyle.None;
            creation.Insert(0, title);
            // Scope and creation actions share the same compact strip.
            creation.Insert(1, controls["ZoneCreateScope"].Element);
            controls["AiToolsScroll"].Element.style.flexShrink = 0;
            controls["ToolActionsScroll"].Element.style.borderTopWidth = 1;
            controls["ToolActionsScroll"].Element.style.borderTopColor = new Color(.25f, .27f, .25f);
            EditorToolWindowStyle.Apply(controls["Library"].Element);
            if (tool == "Scene")
                SeparateSceneTools(controls);
            else if (tool is "Hazards" or "Zones")
            {
                var actions = new VisualElement { name = "ScopedCreationActions" };
                EditorControlLayout.Row(actions);
                actions.style.width = Length.Percent(100);
                EditorToolWindowStyle.Divider(actions);
                foreach (
                    var id in tool == "Hazards"
                        ? new[] { "AddMinefield", "AddClaymore", "AddSniper", "AddBarbedWire" }
                        : new[] { "AddBox", "AddSphere" }
                )
                    actions.Add(controls[id].Element);
                creation.Add(actions);
            }
        }

        foreach (
            var (id, title) in new[]
            {
                ("PlacementGroup", "PLACEMENT"),
                ("SceneActionsGroup", "SCENE TARGET"),
                ("RecordActionsGroup", "SELECTION"),
                ("MapRecordActions", "SELECTION"),
                ("MapOrderGroup", "CHECKPOINT ORDER"),
                ("SceneRestoreGroup", "RESTORE / REBIND"),
                ("AiTriggerGroup", "Trigger type"),
                ("AiWaveWaitPreviousGroup", "Wave sequencing"),
                ("AiRosterRoleGroup", "Bot role"),
                ("AiRosterDifficultyGroup", "Difficulty"),
                ("AiPaceGroup", "Movement pace"),
                ("AiCompletionGroup", "At route end"),
                ("EventKindGroup", "Event type"),
                ("MapShapeGroup", "Volume shape"),
            }
        )
        {
            var heading = new Label(title) { pickingMode = PickingMode.Ignore };
            EditorToolWindowStyle.Heading(heading);
            heading.style.width = Length.Percent(100);
            Element(id).Insert(0, heading);
        }
        foreach (var id in new[] { "Inspector", "EnvironmentMenu", "LootConfiguration", "Controls" })
            EditorToolWindowStyle.Apply(Element(id));
        // Keep object-specific settings together, followed by selection actions and optional details.
        foreach (var id in new[] { "RecordActionsGroup", "RecordDetailsToggleGroup", "IdentityGroup", "DetailsGroup" })
            Element("RecordInspector").Add(Element(id));
        Element("MapInspector").Add(Element("MapDetailsGroup"));
        Element("SceneInspector").Insert(0, Element("SceneHeadingGroup"));
        Element("ContainerSelection").style.fontSize = 15;
        Element("ContainerSelection").style.marginBottom = 6;
        Element("ScenePreview").style.height = 140;
        foreach (var id in new[] { "SceneFocusGroup", "ScenePlaceGroup", "SceneEditGroup", "SceneRestoreGroup" })
            EditorToolWindowStyle.Divider(Element(id));
    }

    private static void SeparateSceneTools(Dictionary<string, EditorControl> controls)
    {
        // Full-width filter groups keep dividers attached when tabs or catalog filters are hidden.
        foreach (var id in new[] { "SceneTabs", "SceneFilters" })
        {
            var group = controls[id].Element;
            group.style.width = Length.Percent(100);
            group.style.marginRight = 0;
            group.style.paddingBottom = 6;
            group.style.marginBottom = 6;
            group.style.borderBottomWidth = 1;
            group.style.borderBottomColor = new Color(.30f, .32f, .28f);
        }
        var source = controls["SceneSource"].Element;
        source.style.width = 200;
        source.style.maxWidth = Length.Percent(100);
        source.style.flexGrow = source.style.flexShrink = 0;

        var creation = controls["CreationTools"].Element;
        creation.style.flexDirection = FlexDirection.Row;
        creation.style.alignItems = Align.Center;
        creation.style.flexWrap = Wrap.NoWrap;
        creation.style.maxWidth = StyleKeyword.None;
        var scroll = (ScrollView)controls["ToolActionsScroll"].Element;
        scroll.mode = ScrollViewMode.Horizontal;
        scroll.horizontalScrollerVisibility = ScrollerVisibility.Auto;
        scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
        scroll.style.height = scroll.style.minHeight = scroll.style.maxHeight = 54;
        scroll.style.flexShrink = 0;
        foreach (
            var (name, caption, ids) in new[]
            {
                ("ScenePickActions", "SELECT", new[] { "Pick" }),
                ("ScenePropActions", "PROPS", new[] { "MapMoveObject", "MapCopyObject", "MapHideObject" }),
                ("SceneWorldActions", "WORLD", new[] { "MapBarrier", "MapDoor" }),
            }
        )
        {
            var row = new VisualElement { name = name };
            EditorControlLayout.Row(row);
            row.style.flexWrap = Wrap.NoWrap;
            row.style.maxWidth = StyleKeyword.None;
            row.style.alignSelf = Align.Center;
            if (name != "ScenePickActions")
            {
                row.style.borderLeftWidth = 1;
                row.style.borderLeftColor = new Color(.30f, .32f, .28f);
                row.style.marginLeft = 8;
                row.style.paddingLeft = 8;
            }
            var label = new Label(caption) { pickingMode = PickingMode.Ignore };
            EditorToolWindowStyle.Heading(label);
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.style.marginTop = label.style.marginBottom = 0;
            label.style.marginRight = 8;
            row.Add(label);
            foreach (var id in ids)
            {
                var button = controls[id].Element;
                button.style.whiteSpace = WhiteSpace.NoWrap;
                button.style.maxWidth = StyleKeyword.None;
                row.Add(button);
            }
            creation.Add(row);
        }
    }
}
