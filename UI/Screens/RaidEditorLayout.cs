using System;
using UnityEngine;
using UnityEngine.UI;
using WTT.Campaigns.UI.Controls;

namespace WTT.Campaigns.UI.Screens;

// Shared by the SDK builder and runtime previews. Serialized prefabs use only native uGUI components.
public static partial class RaidEditorLayout
{
    public static readonly string[] Modules = { "Library", "Inspector" };

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
        // Keep text readable at 1280px. The host scales up at higher resolutions, never down.
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        var ui = new UiElements(font);
        var workspace = UiElements.Rect("Workspace", root.transform, 0, 0);
        UiElements.Stretch(workspace); // Deliberately no full-screen graphic: the world remains interactive.
        var title = Edge(workspace, "WorkspaceTitleBar", 0, 0, 0, 40);
        UiElements.Fill(title, new Color(.055f, .06f, .058f, .98f), true);
        Place(ui.Label(title, "WorkspaceTitle", "CAMPAIGN EDITOR", 19, 220, 36).rectTransform, 14, 2, 220, 36);
        Place(ui.Label(title, "Connection", "Connect a draft in the campaign editor", 16, 650, 36).rectTransform, 240, 2, 650, 36);
        RightButton(ui, title, "CloseEditor", "Close", 68, 8);
        RightButton(ui, title, "HelpToggle", "Help", 60, 82);
        RightButton(ui, title, "WindowsToggle", "Windows", 88, 148);
        RightButton(ui, title, "ContextToggle", "Session", 88, 242);
        var tools = Edge(workspace, "TransformToolbar", 0, 40, 0, 40);
        UiElements.Fill(tools, new Color(.09f, .095f, .09f, .98f), true);
        StripButton(ui, tools, "Undo", "Undo", 72, 8);
        StripButton(ui, tools, "Redo", "Redo", 72, 86);
        StripButton(ui, tools, "Move", "Move", 82, 178);
        StripButton(ui, tools, "Rotate", "Rotate", 82, 266);
        StripButton(ui, tools, "Scale", "Resize", 82, 354);
        StripButton(ui, tools, "Snap", "Snap: on", 100, 458);
        var walk = UiElements.Rect("EditorMapToolbar", tools, 410, 40);
        walk.anchorMin = walk.anchorMax = new Vector2(1, .5f);
        walk.anchoredPosition = new Vector2(-213, 0);
        StripButton(ui, walk, "EditorWalk", "Walkthrough", 140, 0);
        StripButton(ui, walk, "EditorReset", "Reset preview", 140, 148);
        var walkStatus = ui.Label(root.transform, "EditorWalkStatus", "Esc to return to editing", 17, 600, 36);
        Place(walkStatus.rectTransform, 16, 12, 600, 36);
        walkStatus.gameObject.SetActive(false);
        walk.gameObject.SetActive(false);
        var rail = UiElements.Rect("CategoryRail", workspace, 72, 0);
        UiElements.Stretch(rail, 0, 0, 80, 64);
        rail.anchorMax = new Vector2(0, 1);
        rail.offsetMax = new Vector2(72, -80);
        UiElements.Fill(rail, new Color(.065f, .07f, .065f, .98f), true);
        var modes = new[] { "Maps", "Zones", "Bindings", "Captures", "Scene" };
        for (var i = 0; i < modes.Length; i++)
        {
            Button(ui, rail, modes[i], modes[i] == "Bindings" ? "Events" : modes[i], 68, 0, 0, 48);
            Place((RectTransform)rail.Find(modes[i]), 2, 8 + i * 54, 68, 48);
        }
        var dock = UiElements.Rect("DockArea", workspace, 0, 0);
        UiElements.Stretch(dock);
        foreach (var id in Modules)
        {
            var width = id == "Library" ? 280 : 360;
            var panel = Panel(dock, id, width, 850, new Vector2(.5f, .5f), Vector2.zero);
            var bar = Edge(panel, id + "TitleBar", 0, 0, 0, 40);
            UiElements.Fill(bar, new Color(.12f, .13f, .12f), true);
            Place(
                ui.Label(bar, id + "Heading", id == "Library" ? "LIBRARY" : "PROPERTIES", 18, width - 145, 36).rectTransform,
                12,
                2,
                width - 145,
                36
            );
            RightButton(ui, bar, id + "Collapse", "–", 32, 6);
            RightButton(ui, bar, id + "Popout", "Detach", 72, 44);
            if (id == "Library")
                BuildLibrary(ui, panel);
            else
                BuildInspector(ui, panel);
        }
        var bottom = Edge(workspace, "ActionBar", 0, 0, 0, 40);
        bottom.anchorMin = new Vector2(0, 0);
        bottom.anchorMax = new Vector2(1, 0);
        bottom.pivot = new Vector2(.5f, 0);
        bottom.anchoredPosition = new Vector2(0, 24);
        UiElements.Fill(bottom, new Color(.085f, .09f, .082f, .98f), true);
        var actions = Row(bottom, "CreationTools", 36, false);
        UiElements.Stretch(actions, 8, 8, 2, 2);
        Horizontal(actions);
        foreach (
            var item in new[]
            {
                ("AddBox", "+ Box", 100),
                ("AddSphere", "+ Sphere", 100),
                ("Capture", "Capture transform", 168),
                ("Pick", "Pick scenery", 130),
                ("MapNew", "+ Layout", 100),
                ("MapStart", "Set start", 100),
                ("MapCheckpoint", "+ Checkpoint", 132),
                ("MapExit", "Set exit", 100),
                ("MapBarrier", "+ Barrier", 110),
                ("MapMoveObject", "Move prop", 114),
                ("MapCopyObject", "Copy prop", 114),
                ("MapHideObject", "Hide prop", 114),
                ("MapDoor", "Door state", 126),
            }
        )
        {
            Button(ui, actions, item.Item1, item.Item2, item.Item3, 0, 0, 32);
            Fixed(actions.Find(item.Item1).gameObject, item.Item3, 32);
        }
        var capture = UiElements.Rect("CaptureTask", bottom, 0, 0);
        UiElements.Stretch(capture, 8, 8, 0, 0);
        Place(ui.Label(capture, "CaptureRequest", "Complete the requested capture", 16, 800, 36).rectTransform, 4, 2, 800, 36);
        RightButton(ui, capture, "Complete", "Complete capture", 180, 146);
        RightButton(ui, capture, "Cancel", "Cancel", 130, 8);
        capture.gameObject.SetActive(false);
        var status = Edge(workspace, "StatusBar", 0, 0, 0, 24);
        status.anchorMin = new Vector2(0, 0);
        status.anchorMax = new Vector2(1, 0);
        status.pivot = new Vector2(.5f, 0);
        UiElements.Fill(status, new Color(.025f, .03f, .026f, .98f), true);
        Place(ui.Label(status, "Status", "Connecting…", 14, 700, 24).rectTransform, 12, 0, 700, 24);
        var request = ui.Label(status, "Request", "RAID CONTINUES · RMB fly · Ctrl+F8 close", 14, 570, 24).rectTransform;
        request.anchorMin = request.anchorMax = new Vector2(1, .5f);
        request.anchoredPosition = new Vector2(-293, 0);
        var menus = UiElements.Rect("MenuLayer", root.transform, 0, 0);
        UiElements.Stretch(menus);
        var windows = Menu(ui, menus, "WindowsMenu", 250, 152);
        MenuButton(ui, windows, "LibraryToggle", "Library", 8);
        MenuButton(ui, windows, "InspectorToggle", "Properties", 54);
        MenuButton(ui, windows, "ResetLayout", "Reset layout", 100);
        var context = Menu(ui, menus, "ContextMenu", 310, 60);
        MenuButton(ui, context, "EditorUnload", "Unload map / return home", 8);
        var help = Menu(ui, menus, "Controls", 560, 210);
        Place(ui.Label(help, "HelpHeading", "EDITOR CONTROLS", 22, 532, 36).rectTransform, 14, 10, 532, 36);
        Place(
            ui.Label(
                help,
                "Help",
                "RMB + WASD: fly · Q / E: elevation\nFly m/s: camera speed · Shift: 4x · Ctrl: ¼ speed\nDrag handles · Alt: bypass snapping · Ctrl+Z/Y: undo/redo\nEscape dismisses menus, cancels a tool, then closes.\nDetach a panel to move it. Windows restores hidden panels.",
                17,
                532,
                152
            ).rectTransform,
            14,
            50,
            532,
            152
        );
        var tooltip = Panel(root.transform, "EditorTooltip", 600, 72, new Vector2(.5f, .5f), Vector2.zero);
        tooltip.GetComponent<Image>().raycastTarget = false;
        var tooltipText = ui.Label(tooltip, "EditorTooltipText", "", 16, 576, 60);
        tooltipText.alignment = TextAnchor.MiddleLeft;
        tooltip.gameObject.SetActive(false);
        var shield = UiElements.Rect("ConflictShield", root.transform, 0, 0);
        UiElements.Stretch(shield);
        UiElements.Fill(shield, new Color(0, 0, 0, .65f), true);
        BuildConflict(ui, shield);
        shield.gameObject.SetActive(false);
        BuildEditorHome(ui, root.transform);
        EnsureEnvironment(root, ui);
        EnsureNavigation(root, ui);
        foreach (var graphic in root.GetComponentsInChildren<Image>(true))
        {
            if (graphic.name.EndsWith("TitleBar"))
            {
                graphic.sprite = header;
                graphic.type = Image.Type.Sliced;
            }
            if (
                !graphic.GetComponent<Button>()
                && Array.IndexOf(Modules, graphic.name) < 0
                && graphic.name != "Conflict"
                && graphic.name != "EditorHome"
            )
                continue;
            var frame = UiElements.Rect("Frame", graphic.transform, 0, 0);
            UiElements.Stretch(frame);
            var outline = UiElements.Fill(frame, new Color(.55f, .56f, .50f, .38f));
            outline.sprite = border;
            outline.type = Image.Type.Sliced;
            outline.fillCenter = false;
        }
        EnsureTools(root, ui);
        foreach (var feedback in root.GetComponentsInChildren<UiButtonFeedback>(true))
            UnityEngine.Object.DestroyImmediate(feedback);
        return root;
    }

    private static void BuildLibrary(UiElements ui, RectTransform panel)
    {
        Place(ui.Input(panel, "Search", "Search records", 256, 0, 0).GetComponent<RectTransform>(), 12, 52, 256, 36);
        var tabs = Edge(panel, "SceneTabs", 8, 94, 8, 36);
        StripButton(ui, tabs, "SceneCatalog", "Catalog", 80, 0);
        StripButton(ui, tabs, "SceneExisting", "In scene", 80, 86);
        StripButton(ui, tabs, "SceneChanges", "Changes", 80, 172);
        var filters = Edge(panel, "SceneFilters", 8, 134, 8, 36);
        StripButton(ui, filters, "SceneProps", "Props", 80, 0);
        StripButton(ui, filters, "SceneLoot", "Loot", 80, 86);
        StripButton(ui, filters, "ScenePresets", "Presets", 80, 172);
        tabs.gameObject.SetActive(false);
        filters.gameObject.SetActive(false);
        var scroll = ui.Scroll(panel, "LibraryScroll", 260, 550, 0, 0);
        UiElements.Stretch((RectTransform)scroll.transform, 8, 8, 100, 78);
        scroll.horizontal = false;
        scroll.scrollSensitivity = 30;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.GetComponent<Image>().color = Color.clear;
        var layout = scroll.content.GetComponent<VerticalLayoutGroup>();
        layout.spacing = 6;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandHeight = false;
        for (var i = 0; i < 10; i++)
        {
            Button(ui, scroll.content, "Row" + i, i == 0 ? "Select a record" : "", 250, 0, 0, 48);
            var row = scroll.content.Find("Row" + i);
            Fixed(row.gameObject, 250, 48);
            var text = row.GetComponentInChildren<Text>();
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            UiElements.Stretch(text.rectTransform, 8, 8, 2, 2);
            text.fontSize = 16;
            var icon = UiElements.Rect("SceneIcon" + i, row, 40, 40).gameObject.AddComponent<RawImage>();
            icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0, .5f);
            icon.rectTransform.anchoredPosition = new Vector2(24, 0);
            icon.raycastTarget = false;
            icon.gameObject.SetActive(false);
            var status = ui.Label(row, "SceneIconStatus" + i, "…", 12, 40, 40);
            status.rectTransform.anchorMin = status.rectTransform.anchorMax = new Vector2(0, .5f);
            status.rectTransform.anchoredPosition = new Vector2(24, 0);
            status.alignment = TextAnchor.MiddleCenter;
            status.gameObject.SetActive(false);
            if (i > 0)
                row.gameObject.SetActive(false);
        }
        var paging = Edge(panel, "Paging", 0, 0, 0, 42);
        paging.anchorMin = Vector2.zero;
        paging.anchorMax = new Vector2(1, 0);
        paging.pivot = new Vector2(.5f, 0);
        StripButton(ui, paging, "Previous", "Previous", 118, 12);
        StripButton(ui, paging, "Next", "Next", 118, 148);
        var count = ui.Label(panel, "LibraryCount", "No records", 14, 256, 26).rectTransform;
        count.anchorMin = count.anchorMax = new Vector2(.5f, 0);
        count.anchoredPosition = new Vector2(0, 58);
    }

    private static void BuildInspector(UiElements ui, RectTransform panel)
    {
        var scroll = ui.Scroll(panel, "PropertyScroll", 360, 700, 0, 0);
        UiElements.Stretch((RectTransform)scroll.transform, 8, 8, 48, 8);
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        scroll.horizontal = false;
        scroll.scrollSensitivity = 30;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.GetComponent<Image>().color = Color.clear;
        var layout = scroll.content.GetComponent<VerticalLayoutGroup>();
        layout.spacing = 8;
        layout.padding = new RectOffset(0, 0, 0, 8);
        var scene = Stack(scroll.content, "SceneInspector");
        var preview = Row(scene, "ScenePreviewGroup", 180);
        var image = UiElements.Rect("ScenePreview", preview, 300, 180).gameObject.AddComponent<RawImage>();
        image.raycastTarget = false;
        image.color = Color.clear;
        var previewStatus = ui.Label(preview, "ScenePreviewStatus", "Loading preview…", 16, 300, 180);
        previewStatus.alignment = TextAnchor.MiddleCenter;
        var heading = Row(scene, "SceneHeadingGroup", 64);
        ui.Label(heading, "SceneHeading", "Select an object", 18, 318, 64);
        ActionRow(ui, scene, "ScenePreviewRetryGroup", ("ScenePreviewRetry", "Retry preview"));
        ActionRow(ui, scene, "SceneFocusGroup", ("SceneFrame", "Frame (F)"), ("SceneAnchor", "Anchor: Center"));
        ActionRow(ui, scene, "ScenePlaceGroup", ("ScenePlace", "Place"));
        ActionRow(ui, scene, "SceneEditGroup", ("SceneMove", "Move"), ("SceneRotate", "Rotate"), ("SceneRemove", "Remove"));
        ActionRow(ui, scene, "SceneRestoreGroup", ("SceneRestore", "Restore original"), ("SceneRebind", "Rebind to picked"));
        var info = Row(scene, "SceneInfoGroup", 100);
        ui.Label(info, "SceneInfo", "Choose a layout in Maps, then browse the catalog.", 15, 318, 100);
        scene.gameObject.SetActive(false);
        var common = Stack(scroll.content, "RecordInspector");
        Field(ui, common, "Name", "Name");
        var identity = Row(common, "IdentityGroup", 56);
        ui.Label(identity, "Identity", "Select a record", 14, 318, 56);
        ActionRow(ui, common, "EventKindGroup", ("EventKind", "Event kind: Trigger"));
        Vectors(ui, common, "Position", "POSITION · metres");
        Vectors(ui, common, "Rotation", "ROTATION · degrees");
        Vectors(ui, common, "Size", "BOX DIMENSIONS · metres");
        var radius = Row(common, "RadiusGroup", 62);
        ui.Label(radius, "RadiusLabel", "SPHERE RADIUS · metres", 15, 318, 24, 0, 19);
        ui.Input(radius, "Radius", "Radius", 318, 0, -16);
        ActionRow(ui, common, "PlacementGroup", ("AtFeet", "At player"), ("AtAim", "At aim point"));
        ActionRow(ui, common, "ZoneUsesGroup", ("InZone", "In zone"), ("VisitPlace", "Visit"), ("LeaveItemAtLocation", "Place item"));
        ActionRow(ui, common, "SceneActionsGroup", ("Parent", "Select parent"), ("UseObject", "Use scene target"));
        ActionRow(ui, common, "RecordActionsGroup", ("Duplicate", "Duplicate"), ("Delete", "Delete"));
        var details = Row(common, "DetailsGroup", 190);
        var label = ui.Label(details, "Details", "Draft previews never execute story actions.", 15, 318, 190);
        label.alignment = TextAnchor.UpperLeft;
        BuildMapInspector(ui, scroll.content);
    }

    private static RectTransform Stack(Transform parent, string name)
    {
        var rect = UiElements.Rect(name, parent, 318, 0);
        var layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 8;
        layout.childControlHeight = layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;
        return rect;
    }

    private static RectTransform Row(Transform parent, string name, float height, bool size = true)
    {
        var rect = UiElements.Rect(name, parent, 318, height);
        if (size)
            Fixed(rect.gameObject, 318, height);
        return rect;
    }

    private static void Fixed(GameObject go, float width, float height)
    {
        var e = go.AddComponent<LayoutElement>();
        e.preferredWidth = width;
        e.preferredHeight = height;
        e.minHeight = height;
    }

    private static void Horizontal(RectTransform rect)
    {
        var layout = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 6;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
    }

    private static void Field(UiElements ui, Transform parent, string name, string caption)
    {
        var row = Row(parent, name + "Group", 66);
        ui.Label(row, name + "Label", caption.ToUpperInvariant(), 15, 318, 24, 0, 20);
        ui.Input(row, name, caption, 318, 0, -17);
    }

    private static void ActionRow(UiElements ui, Transform parent, string name, params (string Id, string Caption)[] items)
    {
        var row = Row(parent, name, 36);
        var width = (318f - (items.Length - 1) * 6) / items.Length;
        for (var i = 0; i < items.Length; i++)
            Button(ui, row, items[i].Id, items[i].Caption, width, -159 + width / 2 + i * (width + 6), 0, 36);
    }

    private static void Vectors(UiElements ui, Transform parent, string name, string caption)
    {
        var row = Row(parent, name + "Group", 82);
        ui.Label(row, name + "Caption", caption, 15, 318, 24, 0, 29);
        for (var i = 0; i < 3; i++)
        {
            ui.Label(row, name + "Axis" + i, "XYZ"[i].ToString(), 14, 100, 20, (i - 1) * 108, 7);
            ui.Input(row, name + "XYZ"[i], "0", 100, (i - 1) * 108, -22);
        }
    }

    private static RectTransform Menu(UiElements ui, Transform parent, string name, float width, float height)
    {
        var rect = Panel(parent, name, width, height, new Vector2(1, 1), new Vector2(-width / 2 - 12, -height / 2 - 44));
        rect.gameObject.SetActive(false);
        return rect;
    }

    private static void MenuButton(UiElements ui, RectTransform panel, string name, string caption, float y)
    {
        Button(ui, panel, name, caption, panel.sizeDelta.x - 16, 0, 0, 38);
        Place((RectTransform)panel.Find(name), 8, y, panel.sizeDelta.x - 16, 38);
    }

    private static RectTransform Edge(Transform parent, string name, float left, float top, float right, float height)
    {
        var rect = UiElements.Rect(name, parent, 0, height);
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(.5f, 1);
        rect.offsetMin = new Vector2(left, -top - height);
        rect.offsetMax = new Vector2(-right, -top);
        return rect;
    }

    private static void Place(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(.5f, .5f);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(x + width / 2, -y - height / 2);
    }

    private static void StripButton(UiElements ui, Transform parent, string name, string caption, float width, float x)
    {
        Button(ui, parent, name, caption, width, 0, 0, 32);
        Place((RectTransform)parent.Find(name), x, 4, width, 32);
    }

    private static void RightButton(UiElements ui, Transform parent, string name, string caption, float width, float right)
    {
        Button(ui, parent, name, caption, width, 0, 0, 32);
        var rect = (RectTransform)parent.Find(name);
        rect.anchorMin = rect.anchorMax = new Vector2(1, .5f);
        rect.anchoredPosition = new Vector2(-right - width / 2, 0);
    }

    private static RectTransform Panel(Transform parent, string name, float width, float height, Vector2 anchor, Vector2 position)
    {
        var rect = UiElements.Rect(name, parent, width, height);
        rect.anchorMin = rect.anchorMax = anchor;
        rect.anchoredPosition = position;
        UiElements.Fill(rect, new Color(.045f, .05f, .045f, .98f), true);
        return rect;
    }

    private static void Button(UiElements ui, Transform parent, string name, string text, float width, float x, float y, float height = 40)
    {
        var button = ui.Button(parent, text, width, x, y, () => { }, height);
        button.name = name;
        button.GetComponentInChildren<Text>().fontSize = 16;
        if (width <= 72)
            button.GetComponentInChildren<Text>().fontSize = 14;
        button.GetComponentInChildren<Text>().rectTransform.sizeDelta = new Vector2(width - 8, height - 4);
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
