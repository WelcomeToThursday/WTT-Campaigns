using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WTT.Campaigns.UI.Media;

namespace WTT.Campaigns.UI.Controls;

public sealed class EditorTarkovTheme
{
    public static readonly IReadOnlyDictionary<string, string> Icons = new Dictionary<string, string>
    {
        ["Undo"] = "undo-rounded",
        ["Redo"] = "redo-rounded",
        ["Move"] = "open-with-rounded",
        ["Rotate"] = "rotate-right-rounded",
        ["Scale"] = "expand-content-rounded",
        ["Snap"] = "grid-on-rounded",
        ["EditorWalk"] = "directions-walk-rounded",
        ["EditorReset"] = "restart-alt-rounded",
        ["Maps"] = "map-outline-rounded",
        ["Routes"] = "directions-walk-rounded",
        ["Zones"] = "deployed-code-outline-rounded",
        ["Bindings"] = "bolt-rounded",
        ["Captures"] = "photo-camera-outline-rounded",
        ["Scene"] = "forest-outline-rounded",
        ["CloseEditor"] = "close-rounded",
        ["HelpToggle"] = "help-outline-rounded",
        ["WindowsToggle"] = "view-sidebar-outline-rounded",
        ["ContextToggle"] = "tune-rounded",
        ["LibraryCollapse"] = "remove-rounded",
        ["InspectorCollapse"] = "remove-rounded",
        ["LibraryPopout"] = "open-in-new-rounded",
        ["InspectorPopout"] = "open-in-new-rounded",
        ["AddBox"] = "deployed-code-outline-rounded",
        ["AddSphere"] = "circle-outline-rounded",
        ["Capture"] = "add-a-photo-outline-rounded",
        ["Pick"] = "touch-app-outline-rounded",
        ["MapNew"] = "add-location-alt-outline-rounded",
        ["MapStart"] = "flag-outline-rounded",
        ["MapCheckpoint"] = "location-on-outline-rounded",
        ["MapExit"] = "logout-rounded",
        ["MapBarrier"] = "block-rounded",
        ["MapMoveObject"] = "open-with-rounded",
        ["MapCopyObject"] = "content-copy-rounded",
        ["MapHideObject"] = "visibility-off-outline-rounded",
        ["MapDoor"] = "door-front-outline-rounded",
    };

    public static readonly Color Surface = new Color(.075f, .080f, .077f, .98f);
    public static readonly Color Container = new Color(.125f, .13f, .125f);
    public static readonly Color Border = new Color(.38f, .39f, .36f, .8f);
    public static readonly Color Ink = new Color(.80f, .82f, .80f);
    public static readonly Color Muted = new Color(.49f, .52f, .51f);
    public static readonly Color Selected = new Color(.30f, .28f, .20f);
    public static readonly Color Danger = new Color(.25f, .025f, .02f);

    private sealed class Control
    {
        public Button Button = null!;
        public Text? Label;
        public Image? Icon;
    }

    private readonly List<Control> _buttons = new List<Control>();

    public static void Frame(RectTransform rect)
    {
        var frame = rect.Find("ToolFrame") as RectTransform ?? UiElements.Rect("ToolFrame", rect, 0, 0);
        UiElements.Stretch(frame);
        for (var i = 0; i < 4; i++)
        {
            var line = frame.Find("Edge" + i) as RectTransform ?? UiElements.Rect("Edge" + i, frame, 0, 0);
            UiElements.Stretch(line);
            if (i < 2)
            {
                line.anchorMin = new Vector2(i, 0);
                line.anchorMax = new Vector2(i, 1);
                line.sizeDelta = new Vector2(1, 0);
                line.anchoredPosition = new Vector2(i == 0 ? .5f : -.5f, 0);
            }
            else
            {
                line.anchorMin = new Vector2(0, i - 2);
                line.anchorMax = new Vector2(1, i - 2);
                line.sizeDelta = new Vector2(0, 1);
                line.anchoredPosition = new Vector2(0, i == 2 ? .5f : -.5f);
            }
            UiElements.Fill(line, Border);
        }
    }

    public void Apply(GameObject root)
    {
        // Normalize serialized frames too, keeping every border pixel inside its panel.
        foreach (var frame in root.GetComponentsInChildren<RectTransform>(true))
            if (frame.name == "ToolFrame")
                Frame((RectTransform)frame.parent);
        foreach (var label in root.GetComponentsInChildren<Text>(true))
        {
            // Keep the recovered EFT font supplied by the bundle.
            label.color = label.color == UiElements.Muted ? Muted : Ink;
            label.enabled = true;
        }
        foreach (var image in root.GetComponentsInChildren<Image>(true))
        {
            if (image.name == "Frame")
            {
                image.enabled = true;
                image.color = Border;
                continue;
            }
            if (image.color.a == 0 || image.name == "ConflictShield" || image.transform.parent?.name == "ToolFrame")
                continue;
            if (image.name.EndsWith("TitleBar"))
            {
                image.color = new Color(.13f, .14f, .135f);
                if (image.name == "WorkspaceTitleBar")
                {
                    // The recovered footer sprite adds decorative edges to this shallow bar.
                    image.sprite = null;
                    image.type = Image.Type.Simple;
                }
            }
            else if (image.GetComponent<InputField>())
                image.color = new Color(.027f, .03f, .028f);
            else if (image.name == "Handle")
                image.color = Muted;
            else if (!image.GetComponent<Button>())
                image.color = Surface;
        }
        foreach (var input in root.GetComponentsInChildren<InputField>(true))
        {
            Frame((RectTransform)input.transform);
            UiElements.Stretch(input.textComponent.rectTransform, 8, 8, 2, 2);
            if (input.placeholder)
                UiElements.Stretch(input.placeholder.rectTransform, 8, 8, 2, 2);
            input.textComponent.fontSize = 16;
            if (input.placeholder is Text text)
            {
                text.fontSize = 16;
                text.color = Muted;
            }
            input.selectionColor = new Color(.56f, .52f, .35f, .45f);
        }
        foreach (var button in root.GetComponentsInChildren<Button>(true))
        {
            var danger =
                button.name.Contains("Delete")
                || button.name.Contains("Remove")
                || button.name.EndsWith("Close")
                || button.name.EndsWith("Collapse")
                || button.name == "CloseEditor";
            button.targetGraphic.color = danger ? Danger : Container;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.35f, 1.35f, 1.25f);
            colors.selectedColor = colors.normalColor;
            colors.pressedColor = new Color(.7f, .7f, .65f);
            colors.disabledColor = new Color(.5f, .5f, .5f, .6f);
            colors.fadeDuration = .08f;
            button.colors = colors;
            var label = button.GetComponentInChildren<Text>(true);
            var control = new Control { Button = button, Label = label };
            if (label)
            {
                UiElements.Stretch(label.rectTransform, 5, 5, 1, 1);
                label.fontSize = 15;
            }
            if (Icons.TryGetValue(button.name, out var iconName))
            {
                var rect = UiElements.Rect("ToolIcon", button.transform, 18, 18);
                rect.anchorMin = rect.anchorMax = new Vector2(0, .5f);
                rect.anchoredPosition = new Vector2(14, 0);
                var icon = UiElements.Fill(rect, Ink);
                icon.sprite = EditorMaterialArtwork.Load(iconName);
                icon.preserveAspect = true;
                control.Icon = icon;
                if (label)
                    UiElements.Stretch(label.rectTransform, 28, 5, 1, 1);
                // A title-bar close keeps its familiar icon without duplicating the X.
                if (button.name.EndsWith("Collapse"))
                {
                    rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
                    rect.anchoredPosition = Vector2.zero;
                    icon.sprite = EditorMaterialArtwork.Load("close-rounded");
                    if (label)
                        label.enabled = false;
                }
            }
            _buttons.Add(control);
        }
        Refresh();
    }

    public void Refresh()
    {
        foreach (var control in _buttons)
        {
            var button = control.Button;
            if (!button || !button.gameObject.activeInHierarchy)
                continue;
            var graphic = button.targetGraphic;
            if (graphic.color == new Color(.18f, .18f, .15f))
                graphic.color = Container;
            else if (graphic.color == new Color(.36f, .33f, .23f))
                graphic.color = Selected;
            var text = control.Label;
            var ink =
                !button.interactable ? Muted
                : graphic.color == Selected ? UiElements.Ink
                : Ink;
            if (text)
                text!.color = ink;
            if (control.Icon)
                control.Icon!.color = ink;
        }
    }
}
