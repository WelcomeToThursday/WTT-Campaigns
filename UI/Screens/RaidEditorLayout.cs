using System;
using SeasonalPerks.UI.Controls;
using UnityEngine;
using UnityEngine.UI;

namespace SeasonalPerks.UI.Screens;

// Shared by the CJ-SDK prefab builder and visual previews. The bundle contains only native uGUI components.
public static class RaidEditorLayout
{
    public static GameObject Build(Font font)
    {
        var root = new GameObject(
            "SeasonalRaidEditor",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster)
        );
        root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        root.GetComponent<Canvas>().sortingOrder = 32100;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 1f;
        var ui = new UiElements(font);
        var left = Panel(root.transform, "Library", 338, 950, new Vector2(0, .5f), new Vector2(185, 0));
        var right = Panel(root.transform, "Inspector", 424, 950, new Vector2(1, .5f), new Vector2(-228, 0));
        ui.Label(left, "Title", "SEASON AUTHORING", 23, 306, 36, 0, 440);
        ui.Label(left, "Connection", "Connect a draft in the season editor", 16, 306, 50, 0, 392);
        Button(ui, left, "Zones", "Zones", 72, -117, 344);
        Button(ui, left, "Bindings", "Events", 72, -39, 344);
        Button(ui, left, "Captures", "Captures", 72, 39, 344);
        Button(ui, left, "Scene", "Scene", 72, 117, 344);
        ui.Input(left, "Search", "Search records / scene paths", 306, 0, 294);
        for (var i = 0; i < 10; i++)
        {
            Button(ui, left, "Row" + i, i == 0 ? "Select a record" : "", 306, 0, 238 - i * 43, 39);
        }

        Button(ui, left, "Previous", "Previous", 145, -80, -211);
        Button(ui, left, "Next", "Next", 145, 80, -211);
        Button(ui, left, "AddBox", "+ Box", 145, -80, -265);
        Button(ui, left, "AddSphere", "+ Sphere", 145, 80, -265);
        Button(ui, left, "Capture", "Capture transform", 306, 0, -313);
        Button(ui, left, "Pick", "Pick scene object", 306, 0, -361);
        Button(ui, left, "Undo", "Undo", 145, -80, -417);
        Button(ui, left, "Redo", "Redo", 145, 80, -417);
        ui.Label(right, "InspectorTitle", "PROPERTIES", 23, 388, 36, 0, 440);
        ui.Input(right, "Name", "Record name", 388, 0, 393);
        ui.Label(right, "Identity", "Select a zone, event or capture", 14, 388, 40, 0, 345);
        Button(ui, right, "EventKind", "Event kind: Trigger", 388, 0, 345);
        VectorFields(ui, right, "Position", "Position · metres", 300);
        VectorFields(ui, right, "Rotation", "Rotation · degrees", 207);
        VectorFields(ui, right, "Size", "Box dimensions · metres", 114);
        ui.Label(right, "RadiusLabel", "Sphere radius", 17, 175, 32, -100, 24);
        ui.Input(right, "Radius", "Radius", 183, 101, 24);
        Button(ui, right, "Move", "Move", 87, -148, -29);
        Button(ui, right, "Rotate", "Rotate", 87, -50, -29);
        Button(ui, right, "Scale", "Resize", 87, 50, -29);
        Button(ui, right, "Snap", "Snap: on", 87, 148, -29);
        Button(ui, right, "AtFeet", "At player", 185, -100, -80);
        Button(ui, right, "AtAim", "At aim point", 185, 100, -80);
        Button(ui, right, "InZone", "In zone", 122, -133, -130);
        Button(ui, right, "VisitPlace", "Visit", 122, 0, -130);
        Button(ui, right, "LeaveItemAtLocation", "Place item", 122, 133, -130);
        ui.Label(right, "Details", "Draft previews never execute quests or story actions.", 15, 388, 110, 0, -211);
        Button(ui, right, "Parent", "Select parent", 185, -100, -293);
        Button(ui, right, "UseObject", "Use scene target", 185, 100, -293);
        Button(ui, right, "Duplicate", "Duplicate", 185, -100, -343);
        Button(ui, right, "Delete", "Delete", 185, 100, -343);
        Button(ui, right, "Complete", "Complete capture", 250, -68, -409);
        Button(ui, right, "Cancel", "Cancel", 124, 132, -409);
        var top = Panel(root.transform, "RequestBar", 660, 86, new Vector2(.5f, 1), new Vector2(-35, -63));
        ui.Label(top, "Request", "RAID CONTINUES · Player remains in place", 20, 630, 35, 0, 20);
        ui.Label(top, "Status", "Ctrl+F8 to close · Hold right mouse to fly", 16, 630, 35, 0, -20);
        var bottom = Panel(root.transform, "Controls", 710, 78, new Vector2(.5f, 0), new Vector2(-35, 58));
        ui.Label(
            bottom,
            "Help",
            "WASD / Q E · Shift boost · RMB look\nDrag axis handles · Ctrl+Z / Ctrl+Y · Escape cancels, then closes",
            17,
            680,
            70
        );
        var conflict = Panel(root.transform, "Conflict", 670, 620, new Vector2(.5f, .5f), new Vector2(-35, 0));
        ui.Label(conflict, "ConflictTitle", "DRAFT CONFLICT", 25, 620, 45, 0, 270);
        ui.Label(conflict, "ConflictPath", "Another editor changed this record", 16, 620, 55, 0, 215);
        ui.Label(conflict, "LocalCaption", "THIS EDITOR", 18, 620, 30, 0, 170);
        Multiline(ui, conflict, "LocalConflict", 65);
        ui.Label(conflict, "RemoteCaption", "SERVER", 18, 620, 30, 0, -43);
        Multiline(ui, conflict, "RemoteConflict", -150);
        Button(ui, conflict, "KeepLocal", "Keep my conflicts", 290, -157, -272);
        Button(ui, conflict, "KeepRemote", "Keep server conflicts", 290, 157, -272);
        conflict.gameObject.SetActive(false);
        right.Find("EventKind").gameObject.SetActive(false);
        // Runtime adds feedback components with the game's sound callback. No mod scripts enter the asset bundle.
        foreach (var feedback in root.GetComponentsInChildren<UiButtonFeedback>(true))
        {
            UnityEngine.Object.DestroyImmediate(feedback);
        }

        return root;
    }

    private static RectTransform Panel(Transform parent, string name, float width, float height, Vector2 anchor, Vector2 position)
    {
        var rect = UiElements.Rect(name, parent, width, height);
        rect.anchorMin = rect.anchorMax = anchor;
        rect.anchoredPosition = position;
        UiElements.Fill(rect, new Color(.045f, .047f, .042f, .96f), true);
        return rect;
    }

    private static void Button(UiElements ui, Transform parent, string name, string text, float width, float x, float y, float height = 40)
    {
        var button = ui.Button(parent, text, width, x, y, () => { }, height);
        button.name = name;
        var label = button.GetComponentInChildren<Text>();
        label.resizeTextForBestFit = true;
        label.resizeTextMinSize = 12;
        label.resizeTextMaxSize = 18;
    }

    private static void VectorFields(UiElements ui, Transform parent, string name, string caption, float y)
    {
        ui.Label(parent, name + "Caption", caption, 17, 388, 30, 0, y);
        for (var i = 0; i < 3; i++)
        {
            ui.Input(parent, name + "XYZ"[i], "XYZ"[i] + "", 122, (i - 1) * 133, y - 40);
        }
    }

    private static void Multiline(UiElements ui, Transform parent, string name, float y)
    {
        var field = ui.Input(parent, name, "", 620, 0, y);
        ((RectTransform)field.transform).sizeDelta = new Vector2(620, 160);
        UiElements.Stretch(field.textComponent.rectTransform, 12, 12, 8, 8);
        field.textComponent.fontSize = 14;
        field.textComponent.alignment = TextAnchor.UpperLeft;
        field.lineType = InputField.LineType.MultiLineNewline;
        field.readOnly = true;
        field.characterLimit = 0;
    }
}
