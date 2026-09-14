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
        foreach (var child in root.GetComponentsInChildren<Transform>(true))
            if (child.name == "Routes") return;
        var workspace = root.transform.Find("Workspace");
        var rail = workspace.Find("CategoryRail");
        Button(ui, rail, "Routes", "Routes", 68, 0, 0, 48);
        var modes = new[] { "Maps", "Routes", "Zones", "Bindings", "Captures", "Scene" };
        for (var i = 0; i < modes.Length; i++)
            Place((RectTransform)rail.Find(modes[i]), 2, 8 + i * 54, 68, 48);

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
            if (child.name == "MapInspector") { map = child; break; }
        var guide = Row(map, "RouteGuideGroup", 96);
        guide.SetAsFirstSibling();
        var text = ui.Label(guide, "RouteGuide", "", 15, 318, 96);
        text.alignment = TextAnchor.UpperLeft;
        ActionRow(ui, map, "RouteFrameGroup", ("RouteFrame", "Frame waypoint"));
        guide.gameObject.SetActive(false);
        map.Find("RouteFrameGroup").gameObject.SetActive(false);
    }
}
