using UnityEngine;
using UnityEngine.UI;
using WTT.Campaigns.UI.Controls;

namespace WTT.Campaigns.UI.Screens;

public static partial class RaidEditorLayout
{
    private static void BuildEditorHome(UiElements ui, Transform root)
    {
        var home = Panel(root, "EditorHome", 860, 860, new Vector2(.5f, .5f), Vector2.zero);
        ui.Label(home, "EditorHomeTitle", "CAMPAIGN EDITOR", 28, 790, 48, 0, 379);
        ui.Label(
            home,
            "EditorHomeHelp",
            "Spatial authoring workspace\nChoose a draft and map. Gameplay and progression are disabled.",
            19,
            790,
            66,
            0,
            307
        );
        Button(ui, home, "EditorDraft", "Select draft", 790, 0, 231);
        Button(ui, home, "EditorLayout", "Layout: new layout in map", 790, 0, 175);
        Button(ui, home, "EditorMap", "Select map", 790, 0, 119);
        Button(ui, home, "EditorOpen", "Open map", 790, 0, 48);
        Button(ui, home, "EditorRefresh", "Refresh drafts", 380, -204, -12);
        Button(ui, home, "EditorWeb", "Open web Creator", 380, 204, -12);
        Button(ui, home, "EditorStartup", "Startup mode: Normal", 790, 0, -73);
        ui.Label(home, "EditorHomeStatus", "Connecting…", 18, 790, 150, 0, -146);
        Button(ui, home, "EditorRetry", "Retry connection", 380, -204, -267);
        Button(ui, home, "EditorReturn", "Return to game", 380, 204, -267);
        home.gameObject.SetActive(false);
        var tools = Panel(root, "EditorMapToolbar", 710, 112, new Vector2(.5f, 1), new Vector2(350, -80));
        Button(ui, tools, "EditorWalk", "Start walkthrough", 220, -230, 25);
        Button(ui, tools, "EditorReset", "Reset preview", 220, 0, 25);
        Button(ui, tools, "EditorUnload", "Unload map", 220, 230, 25);
        ui.Label(tools, "EditorWalkStatus", "EDITOR MODE · Gameplay disabled", 16, 680, 38, 0, -26);
        tools.gameObject.SetActive(false);
    }

    private static void BuildMapInspector(UiElements ui, Transform parent)
    {
        var map = UiElements.Rect("MapInspector", parent, 414, 872, 0, -12);
        UiElements.Fill(map, new Color(.045f, .047f, .042f), true);
        ui.Input(map, "MapName", "Layout or record name", 388, 0, 378);
        Button(ui, map, "MapNew", "+ Layout", 122, -133, 324);
        Button(ui, map, "MapCopy", "Duplicate", 122, 0, 324);
        Button(ui, map, "MapDelete", "Delete", 122, 133, 324);
        foreach (var row in new[] { "Position", "Rotation", "Size" })
        {
            var y =
                row == "Position" ? 275
                : row == "Rotation" ? 191
                : 107;
            ui.Label(map, "Map" + row + "Label", row == "Size" ? "Size / copy scale" : row, 17, 388, 30, 0, y);
            for (var i = 0; i < 3; i++)
                ui.Input(map, "Map" + row + "XYZ"[i], "XYZ"[i] + "", 122, (i - 1) * 133, y - 34);
        }
        Button(ui, map, "MapStart", "Set start", 122, -133, 13);
        Button(ui, map, "MapCheckpoint", "+ Checkpoint", 122, 0, 13);
        Button(ui, map, "MapExit", "Set exit", 122, 133, 13);
        Button(ui, map, "MapBarrier", "+ Barrier", 185, -100, -37);
        Button(ui, map, "MapShape", "Shape: box", 185, 100, -37);
        Button(ui, map, "MapMoveObject", "Move picked prop", 185, -100, -87);
        Button(ui, map, "MapCopyObject", "Copy picked prop", 185, 100, -87);
        Button(ui, map, "MapHideObject", "Hide picked prop", 185, -100, -137);
        Button(ui, map, "MapDoor", "Door state", 185, 100, -137);
        Button(ui, map, "MapRebind", "Rebind to picked", 185, -100, -187);
        Button(ui, map, "MapAtPlayer", "Place at player", 185, 100, -187);
        Button(ui, map, "MapEarlier", "Earlier checkpoint", 185, -100, -237);
        Button(ui, map, "MapLater", "Later checkpoint", 185, 100, -237);
        Button(ui, map, "MapWalkStart", "Walk from marker: off", 388, 0, -287);
        Button(ui, map, "MapTool", "Tool: Move", 185, -100, -337);
        Button(ui, map, "MapSnap", "Snap: on", 185, 100, -337);
        ui.Label(
            map,
            "MapDetails",
            "Pick scenery in Scene, then return to Maps.\nAll map edits belong to the selected layout.",
            14,
            388,
            46,
            0,
            -392
        );
        map.gameObject.SetActive(false);
    }
}
