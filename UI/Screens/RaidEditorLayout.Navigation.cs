using UnityEngine;
using UnityEngine.UI;
using WTT.Campaigns.UI.Controls;

namespace WTT.Campaigns.UI.Screens;

public static partial class RaidEditorLayout
{
    // Upgrade installed prefabs before either controller indexes and binds the controls.
    public static void EnsureNavigation(GameObject root, UiElements ui)
    {
        // Modern bundles have already moved the rail into the Browser window.
        // Keep upgrading older bundles in place so layout management gets its
        // own category even when the route tab was added by an earlier build.
        var hasRoutes = false;
        var hasLayouts = false;
        foreach (var child in root.GetComponentsInChildren<Transform>(true))
        {
            hasRoutes |= child.name == "Routes";
            hasLayouts |= child.name == "Layouts";
        }
        if (hasRoutes && hasLayouts)
        {
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
                if (child.name == "Maps")
                    child.gameObject.SetActive(false);
            return;
        }
        var workspace = root.transform.Find("Workspace");
        var rail = workspace.Find("CategoryRail");
        if (!rail)
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
                if (child.name == "CategoryRail")
                {
                    rail = child;
                    break;
                }
        if (!rail)
            return;
        if (!hasLayouts)
            Button(ui, rail, "Layouts", "Layouts", 68, 0, 0, 48);
        if (!hasRoutes)
            Button(ui, rail, "Routes", "Routes", 68, 0, 0, 48);
        // Maps was the old combined layout/scene workspace. Leave its object
        // in the prefab for serialized compatibility, but remove it from the
        // visible rail once Layouts is available.
        rail.Find("Maps")?.gameObject.SetActive(false);
        var modes = new[] { "Layouts", "Routes", "Zones", "Bindings", "Captures", "Scene" };
        for (var i = 0; i < modes.Length; i++)
            if (rail.Find(modes[i]) is { } mode)
                Place((RectTransform)mode, 2, 8 + i * 54, 68, 48);

        // Route controls were already added by the previous route-tab bundle.
        // Only create the camera fields and route inspector rows when adding
        // Routes to an older prefab; repeated Prepare calls stay idempotent.
        if (!hasRoutes)
        {
            var tools = workspace.Find("TransformToolbar");
            Place(ui.Label(tools, "CameraSpeedLabel", "Fly m/s", 15, 70, 32).rectTransform, 574, 4, 70, 32);
            StripButton(ui, tools, "CameraSlower", "−", 32, 646);
            Place(ui.Input(tools, "CameraSpeed", "6", 72, 0, 0).GetComponent<RectTransform>(), 684, 4, 72, 32);
            StripButton(ui, tools, "CameraFaster", "+", 32, 762);
            root.transform.Find("MenuLayer/Controls/Help").GetComponent<Text>().text =
                "RMB + WASD: fly · Q / E: elevation\nFly m/s: camera speed · Shift: 4x · Ctrl: ¼ speed\n"
                + "Drag handles · Alt: bypass snapping · Ctrl+Z/Y: undo/redo\n"
                + "Escape dismisses menus, cancels a tool, then closes.\nDetach a panel to move it. Windows restores hidden panels.";

            Transform map = null!;
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
                if (child.name == "MapInspector")
                {
                    map = child;
                    break;
                }
            var guide = Row(map, "RouteGuideGroup", 96);
            guide.SetAsFirstSibling();
            var text = ui.Label(guide, "RouteGuide", "", 15, 318, 96);
            text.alignment = TextAnchor.UpperLeft;
            ActionRow(ui, map, "RouteFrameGroup", ("RouteFrame", "Frame waypoint"));
            guide.gameObject.SetActive(false);
            map.Find("RouteFrameGroup").gameObject.SetActive(false);
        }
    }
}
