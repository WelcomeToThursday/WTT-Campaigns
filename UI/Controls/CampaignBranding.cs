using UnityEngine;
using UnityEngine.UI;

namespace WTT.Campaigns.UI.Controls;

public static class CampaignBranding
{
    public static RectTransform Create(Transform parent, Font font, float width, float height)
    {
        var root = UiElements.Rect("CampaignBranding", parent, width, height);
        var ui = new UiElements(font);
        var accent = new Color32(131, 197, 169, 255);
        var mark = ui.Label(root, "Brand", "W T T", Mathf.RoundToInt(height * .15f), width, height * .24f, 0, height * .24f);
        mark.alignment = TextAnchor.MiddleCenter;
        mark.color = accent;
        var title = ui.Label(root, "Title", "CAMPAIGNS", Mathf.RoundToInt(height * .33f), width * .92f, height * .43f, 0, -height * .04f);
        title.alignment = TextAnchor.MiddleCenter;
        title.color = new Color32(225, 237, 223, 255);
        title.resizeTextForBestFit = true;
        title.resizeTextMinSize = Mathf.RoundToInt(height * .20f);
        title.resizeTextMaxSize = title.fontSize;
        UiElements.Fill(UiElements.Rect("Underline", root, width * .60f, 1, 0, -height * .31f), accent);
        return root;
    }
}
