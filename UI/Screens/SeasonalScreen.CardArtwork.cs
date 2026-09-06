using SeasonalPerks.UI.Controls;
using SeasonalPerks.UI.Models;
using SeasonalPerks.UI.Modifiers;
using UnityEngine;
using UnityEngine.UI;

namespace SeasonalPerks.UI.Screens;

public sealed partial class SeasonalScreen
{
    private PerkCardHover CardArtwork(GameObject card, PerkEntry perk)
    {
        var background = (RectTransform)card.transform.Find("Content/Background");
        UiElements.Fill(background, Color.clear);
        // Restore the live layer order and colors from level47-82. The grid is tiled,
        // and the idle tint fades out before the right edge rather than filling the row.
        Image Layer(string node, string? artwork, Color color, bool tiled = false)
        {
            var image = background.Find(node).GetComponent<Image>();
            image.gameObject.SetActive(true);
            UiElements.Stretch(image.rectTransform);
            image.sprite = null;
            image.material = null;
            if (artwork != null)
            {
                ArtworkRequested?.Invoke(artwork, image);
            }
            image.color = color;
            image.type = tiled ? Image.Type.Tiled : Image.Type.Simple;
            image.raycastTarget = false;
            return image;
        }
        var grey = new Color32(149, 158, 163, 255);
        Layer("BackgroundGradient", "modifier-gradient", grey);
        var hover = card.AddComponent<PerkCardHover>();
        if (!perk.Common)
        {
            var sign = perk.Points > 0 ? "Negative" : "Positive";
            var tint =
                perk.Points > 0 ? new Color32(212, 41, 41, 56) : new Color32(112, 176, 53, 56);
            var idle = Layer("Background_Idle_" + sign, "modifier-tint", tint);
            idle.rectTransform.sizeDelta = new Vector2(-450, 0);
            idle.rectTransform.anchoredPosition = new Vector2(-225, 0);
            hover.Idle = idle.gameObject;
            tint.a = 128;
            hover.Selected = Layer("BackgroundSelected_" + sign, null, tint).gameObject;
        }
        hover.Highlight = Layer("Hover", null, new Color32(70, 70, 70, 92)).gameObject;
        Layer("BackgroundGrid", "modifier-grid", grey, true);
        Layer("BackgroundSadow", "modifier-shadow", Color.white);
        hover.Allowed = () => !_busy && _dialog == null && Root.activeInHierarchy;
        hover.Refresh(false);
        return hover;
    }

    private void ClearCardHover()
    {
        foreach (var pair in _cards)
        {
            pair.Card.GetComponent<PerkCardHover>()?.ClearHover();
        }
    }
}
