using System;
using UnityEngine;
using UnityEngine.UI;
using WTT.Campaigns.UI.Controls;

namespace WTT.Campaigns.UI.Screens;

// Shared by the CJ-SDK prefab builder and visual previews. The bundle contains only native uGUI components.
public static partial class RaidEditorLayout
{
    public static GameObject Build(Font font, Sprite border, Sprite header)
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
        scaler.referenceResolution = new Vector2(1920, 1200);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        var ui = new UiElements(font);
        var workspace = Panel(root.transform, "Workspace", 800, 1110, new Vector2(.5f, .5f), new Vector2(-530, 0));
        var title = UiElements.Rect("WorkspaceTitleBar", workspace, 800, 52, 0, 529);
        UiElements.Fill(title, new Color(.16f, .16f, .14f), true);
        ui.Label(title, "WorkspaceTitle", "CAMPAIGN / RAID EDITOR", 22, 410, 44, -180, 0);
        Button(ui, title, "ResetLayout", "Reset layout", 120, 180, 0, 36);
        Button(ui, title, "HelpToggle", "Help", 68, 280, 0, 36);
        Button(ui, title, "CloseEditor", "X", 40, 350, 0, 36);
        var dock = UiElements.Rect("DockArea", workspace, 780, 950, 0, 25);
        foreach (var module in Modules)
        {
            var placeholder = UiElements.Rect(module.Id + "Placeholder", dock, module.Width, 950, module.X, 0);
            ui.Label(
                placeholder,
                module.Id + "Detached",
                module.Caption + "\nOpen in a popout",
                20,
                module.Width - 36,
                75,
                0,
                45
            ).alignment = TextAnchor.MiddleCenter;
            Button(ui, placeholder, module.Id + "Return", "Return to workspace", module.Width - 36, 0, -30);
            placeholder.gameObject.SetActive(false);
            var panel = Panel(dock, module.Id, module.Width, 950, new Vector2(.5f, .5f), new Vector2(module.X, 0));
            module.Build(ui, panel);
            var bar = UiElements.Rect(module.Id + "TitleBar", panel, module.Width, 52, 0, 449);
            UiElements.Fill(bar, new Color(.12f, .12f, .10f), true);
            ui.Label(bar, module.Id + "Heading", module.Caption, 21, module.Width - 120, 42, -48, 0);
            Button(ui, bar, module.Id + "Popout", "Pop out", 90, module.Width / 2 - 55, 0, 34);
        }
        var top = UiElements.Rect("RequestBar", workspace, 780, 98, 0, -499);
        ui.Label(top, "Request", "RAID CONTINUES / Player remains in place", 16, 760, 25, 0, 30);
        ui.Label(top, "Status", "Ctrl+F8 to close / Hold right mouse to fly", 15, 760, 58, 0, -15);
        var bottom = Panel(root.transform, "Controls", 710, 170, new Vector2(.5f, .5f), new Vector2(300, -330));
        ui.Label(bottom, "HelpHeading", "EDITOR CONTROLS", 22, 660, 38, 0, 58);
        ui.Label(
            bottom,
            "Help",
            "WASD / Q E / Shift boost / RMB look\nDrag axis handles / Ctrl+Z / Ctrl+Y\nEscape cancels an action, then closes the editor.\nDrag window headers. Pop out a panel; Dock returns it.",
            17,
            660,
            110,
            0,
            -15
        );
        bottom.gameObject.SetActive(false);
        var shield = UiElements.Rect("ConflictShield", root.transform, 0, 0);
        UiElements.Stretch(shield);
        UiElements.Fill(shield, new Color(0, 0, 0, .65f), true);
        BuildConflict(ui, shield);
        shield.gameObject.SetActive(false);
        BuildEditorHome(ui, root.transform);
        foreach (var image in root.GetComponentsInChildren<Image>(true))
        {
            if (image.name.EndsWith("TitleBar"))
            {
                image.sprite = header;
                image.type = Image.Type.Sliced;
            }
            if (image.name == "ConflictShield")
                continue;
            var frame = UiElements.Rect("Frame", image.transform, 0, 0);
            UiElements.Stretch(frame);
            var outline = frame.gameObject.AddComponent<Image>();
            outline.sprite = border;
            outline.type = Image.Type.Sliced;
            outline.fillCenter = false;
            outline.color = new Color(.55f, .52f, .43f, .65f);
            outline.raycastTarget = false;
        }
        foreach (var feedback in root.GetComponentsInChildren<UiButtonFeedback>(true))
            UnityEngine.Object.DestroyImmediate(feedback);
        return root;
    }

    // Add tool panels here; the runtime window host uses the same registry.
    public sealed class Module
    {
        public readonly string Id,
            Caption;
        public readonly float Width,
            X;
        public readonly Action<UiElements, RectTransform> Build;

        public Module(string id, string caption, float width, float x, Action<UiElements, RectTransform> build)
        {
            Id = id;
            Caption = caption;
            Width = width;
            X = x;
            Build = build;
        }
    }

    public static readonly Module[] Modules =
    {
        new Module("Library", "LIBRARY", 338, -221, BuildLibrary),
        new Module("Inspector", "PROPERTIES", 424, 166, BuildInspector),
    };

    private static void BuildLibrary(UiElements ui, RectTransform left)
    {
        ui.Label(left, "Connection", "Connect a draft in the campaign editor", 16, 306, 50, 0, 392);
        var modes = new[] { "Maps", "Zones", "Bindings", "Captures", "Scene" };
        var widths = new[] { 52, 52, 62, 76, 52 };
        var position = -153f;
        for (var i = 0; i < modes.Length; i++)
        {
            Button(ui, left, modes[i], modes[i] == "Bindings" ? "Events" : modes[i], widths[i], position + widths[i] / 2f, 344);
            var label = left.Find(modes[i]).GetComponentInChildren<Text>();
            label.resizeTextForBestFit = false;
            label.fontSize = 14;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            var labelRect = (RectTransform)label.transform;
            labelRect.sizeDelta = new Vector2(widths[i] - 8, labelRect.sizeDelta.y);
            position += widths[i] + 3;
        }
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
    }

    private static void BuildInspector(UiElements ui, RectTransform right)
    {
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
        right.Find("EventKind").gameObject.SetActive(false);
        BuildMapInspector(ui, right);
    }

    private static void BuildConflict(UiElements ui, Transform parent)
    {
        var conflict = Panel(parent, "Conflict", 670, 620, new Vector2(.5f, .5f), Vector2.zero);
        ui.Label(conflict, "ConflictTitle", "DRAFT CONFLICT", 25, 620, 45, 0, 270);
        ui.Label(conflict, "ConflictPath", "Another editor changed this record", 16, 620, 55, 0, 215);
        ui.Label(conflict, "LocalCaption", "THIS EDITOR", 18, 620, 30, 0, 170);
        Multiline(ui, conflict, "LocalConflict", 65);
        ui.Label(conflict, "RemoteCaption", "SERVER", 18, 620, 30, 0, -43);
        Multiline(ui, conflict, "RemoteConflict", -150);
        Button(ui, conflict, "KeepLocal", "Keep my conflicts", 290, -157, -272);
        Button(ui, conflict, "KeepRemote", "Keep server conflicts", 290, 157, -272);
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
