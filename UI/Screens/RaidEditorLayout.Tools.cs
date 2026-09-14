using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WTT.Campaigns.UI.Controls;

namespace WTT.Campaigns.UI.Screens;

public static partial class RaidEditorLayout
{
    public static void Prepare(GameObject root)
    {
        var ui = new UiElements(root.GetComponentInChildren<Text>(true).font);
        EnsureEnvironment(root, ui);
        EnsureNavigation(root, ui);
        EnsureTools(root, ui);
    }

    // Migrate bundled uGUI controls in place before the client indexes them.
    public static void EnsureTools(GameObject root, UiElements ui)
    {
        if (root.transform.Find("ToolWindows"))
        {
            EnsureZoneControls(root, ui);
            return;
        }
        var controls = new Dictionary<string, Transform>();
        foreach (var child in root.GetComponentsInChildren<Transform>(true))
            if (!controls.ContainsKey(child.name))
                controls.Add(child.name, child);
        Transform C(string id) => controls[id];
        var windows = UiElements.Rect("ToolWindows", root.transform, 0, 0);
        UiElements.Stretch(windows);
        foreach (var id in Modules)
        {
            var panel = (RectTransform)C(id);
            panel.SetParent(windows, false);
            var bar = (RectTransform)C(id + "TitleBar");
            UiElements.Stretch(bar, 0, 0, 0, 0);
            bar.anchorMin = new Vector2(0, 1);
            bar.sizeDelta = new Vector2(0, 30);
            bar.anchoredPosition = new Vector2(0, -15);
            var title = C(id + "Heading").GetComponent<Text>();
            title.text = id == "Library" ? "BROWSER" : "PROPERTIES";
            UiElements.Stretch(title.rectTransform, 10, 42, 0, 0);
            title.fontSize = 17;
            C(id + "Popout").gameObject.SetActive(false);
            var close = (RectTransform)C(id + "Collapse");
            close.anchorMin = close.anchorMax = new Vector2(1, .5f);
            close.sizeDelta = new Vector2(26, 22);
            close.anchoredPosition = new Vector2(-17, 0);
            close.GetComponentInChildren<Text>().text = "×";
            EditorTarkovTheme.Frame(panel);
        }
        var library = C("Library");
        C("Capture").GetComponentInChildren<Text>().text = "Capture";
        var rail = (RectTransform)C("CategoryRail");
        rail.SetParent(library, false);
        Place(rail, 8, 36, 424, 60);
        C("Search").SetParent(library, false);
        Place((RectTransform)C("Search"), 8, 104, 424, 32);
        var creation = (RectTransform)C("CreationTools");
        creation.SetParent(library, false);
        creation.GetComponent<HorizontalLayoutGroup>().enabled = false;
        var actions = (RectTransform)C("ActionBar");
        // This host now contains only a requested capture's completion controls.
        actions.GetComponent<Image>().color = Color.clear;
        var capture = (RectTransform)C("CaptureTask");
        UiElements.Stretch(capture, 8, 8, 0, 0);
        UiElements.Fill(capture, EditorTarkovTheme.Surface, true);
        EditorTarkovTheme.Frame(capture);
        C("LibraryToggle").GetComponentInChildren<Text>().text = "Browser";
        var menu = (RectTransform)C("WindowsMenu");
        menu.sizeDelta = new Vector2(250, 240);
        MenuButton(ui, menu, "EnvironmentWindowToggle", "Environment", 100);
        MenuButton(ui, menu, "HelpWindowToggle", "Help", 146);
        Place((RectTransform)C("ResetLayout"), 8, 192, 234, 36);
        MakeTool(C("EnvironmentMenu"), "ENVIRONMENT", "EnvironmentClose", windows, ui, 360, 580);
        MakeTool(C("Controls"), "EDITOR CONTROLS", "HelpClose", windows, ui, 560, 270);
        C("Help").GetComponent<Text>().text =
            "RMB + WASD: fly · Q / E: elevation\nFly m/s: speed · Shift: 4× · Ctrl: precision\n"
            + "Drag handles · Alt: bypass snap · Ctrl+Z/Y: undo/redo\n"
            + "Drag a title to move a window; drag its lower-right corner to resize.\n"
            + "Windows reopens tools. Reset layout restores their positions.\n"
            + "Escape dismisses menus, releases a field, cancels a tool, then closes.";
        C("Help").GetComponent<Text>().fontSize = 16;
        ((RectTransform)C("Help")).sizeDelta = new Vector2(532, 170);
        C("HelpHeading").gameObject.SetActive(false);
        // Optional details retain their data while collapsed.
        foreach (var pair in new[] { ("RecordInspector", "RecordDetailsToggle"), ("MapInspector", "MapDetailsToggle") })
            ActionRow(ui, C(pair.Item1), pair.Item2 + "Group", (pair.Item2, "Details +"));
        // Frames on the floating toolbar preserve the native, unobtrusive tool-window treatment.
        EditorTarkovTheme.Frame((RectTransform)C("WorkspaceTitleBar"));
        EditorTarkovTheme.Frame((RectTransform)C("TransformToolbar"));
        EnsureZoneControls(root, ui);
    }

    private static void EnsureZoneControls(GameObject root, UiElements ui)
    {
        Transform? Find(string id)
        {
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
                if (child.name == id)
                    return child;
            return null;
        }

        var creation = Find("CreationTools");
        if (creation != null)
        {
            foreach (var id in new[] { "ZoneCreateShared", "ZoneCreateLayout" })
                if (Find(id) is Transform legacyButton)
                    UnityEngine.Object.DestroyImmediate(legacyButton.gameObject);
            if (Find("ZoneCreateScope") == null)
            {
                var dropdown = DropdownField(ui, creation, "ZoneCreateScope", "NEW ZONE SCOPE", root.transform);
                var row = dropdown.transform.parent;
                dropdown.transform.SetParent(creation, false);
                UnityEngine.Object.DestroyImmediate(row.gameObject);
                Fixed(dropdown.gameObject, 200, 32);
            }
        }

        var inspector = Find("RecordInspector");
        if (inspector != null && Find("ZoneScope") == null)
        {
            var legacy = Find("ZoneLayoutGroup");
            if (legacy != null)
                UnityEngine.Object.DestroyImmediate(legacy.gameObject);
            DropdownField(ui, inspector, "ZoneScope", "ZONE SCOPE", root.transform);
        }
    }

    private static void AddCreationButton(Transform parent, UiElements ui, string id, string caption, float width)
    {
        if (parent.Find(id) != null)
            return;
        Button(ui, parent, id, caption, width, 0, 0, 32);
        Fixed(parent.Find(id)!.gameObject, width, 32);
    }

    private static void MakeTool(Transform panel, string title, string closeId, Transform windows, UiElements ui, float width, float height)
    {
        panel.SetParent(windows, false);
        var rect = (RectTransform)panel;
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
        rect.sizeDelta = new Vector2(width, height);
        var children = new List<Transform>();
        foreach (Transform child in panel)
            children.Add(child);
        var scroll = ui.Scroll(panel, panel.name + "Scroll", width - 16, height - 42, 0, -15);
        UiElements.Stretch((RectTransform)scroll.transform, 8, 8, 36, 10);
        scroll.GetComponent<Image>().color = Color.clear;
        var layout = scroll.content.GetComponent<VerticalLayoutGroup>();
        layout.enabled = false;
        scroll.content.GetComponent<ContentSizeFitter>().enabled = false;
        scroll.content.sizeDelta = new Vector2(0, title == "ENVIRONMENT" ? 638 : 210);
        foreach (var child in children)
            child.SetParent(scroll.content, false);
        var bar = UiElements.Rect(panel.name + "TitleBar", panel, 0, 30);
        bar.anchorMin = new Vector2(0, 1);
        bar.anchorMax = Vector2.one;
        bar.anchoredPosition = new Vector2(0, -15);
        UiElements.Fill(bar, EditorTarkovTheme.Container, true);
        var label = ui.Label(bar, panel.name + "Title", title, 17, width - 52, 28);
        UiElements.Stretch(label.rectTransform, 10, 42, 0, 0);
        RightButton(ui, bar, closeId, "×", 26, 4);
        ((RectTransform)bar.Find(closeId)).sizeDelta = new Vector2(26, 22);
        EditorTarkovTheme.Frame(rect);
        panel.gameObject.SetActive(false);
    }
}
