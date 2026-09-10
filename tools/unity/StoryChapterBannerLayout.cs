using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using WTT.Campaigns.UI.Controls;

namespace WTT.Campaigns.Tools
{
    // Recovered level49 MainQuestNotification: 500px banner, chapter art, separate title,
    // status line and trailing state icon. Kept independent of EFT for offline rendering.
    public sealed class StoryChapterBanner
    {
        public readonly RectTransform Root;
        public readonly RectTransform Content;
        public readonly Image Background;
        public readonly Image Icon;
        public readonly TMP_Text Title;
        public readonly TMP_Text Status;
        public readonly Image Checkmark;
        public readonly LayoutElement Layout;
        public readonly CanvasGroup CanvasGroup;

        public StoryChapterBanner(Transform parent, TMP_FontAsset font, string title, string message, string status)
        {
            Root = UiElements.Rect("MainQuestNotification", parent, 500, 50.22f);
            Root.gameObject.SetActive(false);
            CanvasGroup = Root.gameObject.AddComponent<CanvasGroup>();
            Layout = Root.gameObject.AddComponent<LayoutElement>();
            Layout.minHeight = 0;
            Layout.preferredHeight = 50.22f;
            Layout.flexibleHeight = 0;
            var stack = Root.gameObject.AddComponent<VerticalLayoutGroup>();
            stack.padding.bottom = 2;
            stack.childAlignment = TextAnchor.LowerRight;
            stack.childControlWidth = false;
            stack.childControlHeight = true;
            stack.childForceExpandWidth = stack.childForceExpandHeight = false;

            Content = UiElements.Rect("Content", Root, 500, 48.22f);
            Content.pivot = new Vector2(1, 0);
            var contentSize = Content.gameObject.AddComponent<LayoutElement>();
            contentSize.minHeight = 0;
            contentSize.preferredHeight = 48.22f;
            contentSize.flexibleHeight = 0;
            var frame = Art(Content, "notification-frame");
            frame.color = new Color(0, 0, 0, .788f);
            // Live retains these old achievement frames in the hierarchy but disables them.
            frame.enabled = false;
            Content.gameObject.AddComponent<RectMask2D>();
            var row = Content.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding.left = 5;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = true;

            var back = UiElements.Rect("bg", Content, 0, 0);
            UiElements.Stretch(back);
            back.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Background = Art(back, "notification-chapter");
            Background.raycastTarget = true;
            var left = UiElements.Rect("Left", Content, 60, 48.22f);
            var iconSize = left.gameObject.AddComponent<LayoutElement>();
            iconSize.minWidth = iconSize.preferredWidth = 60;
            Art(left, "notification-frame").enabled = false;
            Icon = Art(UiElements.Rect("Icon", left, 56.1584f, 44.9268f, 2.82f), "journal-active");
            Icon.preserveAspect = true;

            var texts = UiElements.Rect("Text group", Content, 435, 48.22f);
            texts.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            var lines = texts.gameObject.AddComponent<VerticalLayoutGroup>();
            lines.childAlignment = TextAnchor.MiddleLeft;
            lines.spacing = -5;
            lines.childControlWidth = lines.childControlHeight = true;
            lines.childForceExpandWidth = true;
            lines.childForceExpandHeight = false;
            Title = Label(texts, "Title", font, title, new Color32(255, 244, 210, 255));
            var statusRow = UiElements.Rect("Text", texts, 435, 26.61f);
            statusRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 26.61f;
            var statusLayout = statusRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            statusLayout.childAlignment = TextAnchor.MiddleLeft;
            statusLayout.spacing = 5;
            statusLayout.childControlWidth = statusLayout.childControlHeight = true;
            statusLayout.childForceExpandWidth = statusLayout.childForceExpandHeight = false;
            Status = Label(statusRow, "Text", font, message, new Color32(182, 229, 243, 255));
            var mark = UiElements.Rect("Icon", statusRow, 30, 24);
            var markLayout = mark.gameObject.AddComponent<LayoutElement>();
            markLayout.minWidth = markLayout.preferredWidth = 30;
            markLayout.preferredHeight = 24;
            Checkmark = Art(
                mark,
                status == "Complete" ? "notification-success"
                    : status == "Failed" ? "notification-failed"
                    : "notification-started"
            );
            Checkmark.preserveAspect = true;
        }

        private static Image Art(RectTransform root, string resource)
        {
            var image = UiElements.Fill(root, Color.white);
            image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                "Assets/Mods/WTT-Campaigns.Assets/"
                    + (
                        resource == "notification-chapter" || resource == "notification-frame"
                            ? "StoryNotifications/Artwork/"
                            : "StoryStatusIcons/"
                    )
                    + resource
                    + ".png"
            );
            return image;
        }

        private static TMP_Text Label(Transform parent, string name, TMP_FontAsset font, string text, Color color)
        {
            var value = UiElements.Rect(name, parent, 435, 26.61f).gameObject.AddComponent<TextMeshProUGUI>();
            value.font = font;
            value.fontSize = 16;
            value.fontStyle = FontStyles.Normal;
            value.alignment = TextAlignmentOptions.Left;
            value.color = color;
            value.richText = false;
            value.enableWordWrapping = false;
            value.overflowMode = TextOverflowModes.Ellipsis;
            value.raycastTarget = false;
            value.text = text;
            value.gameObject.AddComponent<LayoutElement>().preferredHeight = 26.61f;
            return value;
        }
    }
}
