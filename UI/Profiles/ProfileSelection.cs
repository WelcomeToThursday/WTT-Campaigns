using System;
using System.Linq;
using SeasonalPerks.UI.Audio;
using SeasonalPerks.UI.Controls;
using SeasonalPerks.UI.Models;
using UnityEngine;
using UnityEngine.UI;

namespace SeasonalPerks.UI.Profiles;

public sealed class ProfileSelection
{
    private readonly UiElements _ui;
    private readonly Action<string, Image> _artwork;
    public readonly GameObject Root;
    private readonly RectTransform _background;
    private readonly RectTransform _topGlow;

    public ProfileSelection(
        Transform parent,
        Font font,
        ScreenState state,
        Action<string, Image> artwork,
        Action<string, RawImage> model,
        Action<string> select,
        Action edit,
        Action seasonIntro,
        Action close,
        bool startup,
        Material? glowMaterial,
        Action<InterfaceSound>? sounds = null,
        Action<bool>? hoverSound = null
    )
    {
        _ui = new UiElements(font, sounds);
        _artwork = artwork;
        var root = UiElements.Rect("LiveProfileSelection", parent, 1920, 1080);
        Root = root.gameObject;
        _background = Art(root, "background", 1920, 1080, 0, 0).rectTransform;
        var glow = Art(root, "footer-glow", 0, 0, 0, 483);
        glow.rectTransform.anchorMin = new Vector2(.1f, 0);
        glow.rectTransform.anchorMax = new Vector2(.9f, .5f);
        glow.rectTransform.pivot = new Vector2(.5f, 0);
        glow.rectTransform.localRotation = Quaternion.Euler(0, 0, 180);
        glow.color = new Color(1, 1, 1, .282353f);
        _ui.Label(root, "Title", "SELECT PROFILE AND MODE", 42, 1275, 50, 0, 478).alignment = TextAnchor.MiddleCenter;
        root.Find("Title").GetComponent<Text>().color = new Color(.851f, .851f, .851f);
        Card(root, state, "normal", -205, model, select, edit, seasonIntro, hoverSound);
        Card(root, state, "seasonal", 205, model, select, edit, seasonIntro, hoverSound);
        if (!startup)
        {
            var back = _ui.Button(root, "BACK", 120, 830, -488, close);
            back.targetGraphic.color = Color.clear;
        }
        // Live's Environment UI draws this additive layer above the selection canvas.
        var topGlow = Art(root, "top-glow-seasonal", 1920, 512, 0, 540);
        topGlow.rectTransform.pivot = new Vector2(.5f, 1);
        topGlow.color = new Color(1, 1, 1, .6f);
        topGlow.material = glowMaterial;
        _topGlow = topGlow.rectTransform;
    }

    public void Fit(Vector2 viewport)
    {
        // Backgrounds follow the viewport; the two cards retain their native proportions.
        _background.sizeDelta = viewport;
        _topGlow.sizeDelta = new Vector2(viewport.x, 512);
        _topGlow.anchoredPosition = new Vector2(0, viewport.y * .5f);
    }

    private Image Art(Transform parent, string name, float width, float height, float x, float y)
    {
        var image = UiElements.Fill(UiElements.Rect(name, parent, width, height, x, y), Color.white);
        image.enabled = false;
        _artwork(name, image);
        return image;
    }

    private void Card(
        Transform parent,
        ScreenState state,
        string mode,
        float x,
        Action<string, RawImage> model,
        Action<string> select,
        Action edit,
        Action seasonIntro,
        Action<bool>? hoverSound
    )
    {
        var seasonal = mode == "seasonal";
        var character = state.Characters.FirstOrDefault(value => value.Mode == mode) ?? new CharacterEntry { Mode = mode };
        var rect = UiElements.Rect(mode + "-profile", parent, 390, 800, x);
        UiElements.Fill(rect, Color.clear, true);
        var hover = rect.gameObject.AddComponent<ProfileCardHover>();
        hover.Seasonal = seasonal;
        hover.Entered = () => hoverSound?.Invoke(seasonal);
        if (seasonal)
        {
            var idle = UiElements.Rect("SeasonGlowIdle", rect, 664, 1016);
            hover.Idle = idle.gameObject.AddComponent<CanvasGroup>();
            var highlight = UiElements.Rect("SeasonGlowHover", rect, 664, 1016);
            hover.Glow = highlight.gameObject.AddComponent<CanvasGroup>();
            hover.IdleFrames = new Image[3];
            hover.HoverFrames = new Image[3];
            for (var i = 0; i < 3; i++)
            {
                hover.IdleFrames[i] = Art(idle, "seasonal-glow-" + (i + 1), 664, 1016, 0, 0);
                hover.HoverFrames[i] = Art(highlight, "seasonal-hover-" + (i + 1), 664, 1016, 0, i == 0 ? -10 : 0);
            }
            hover.AnimateGlow(0);
        }
        else
        {
            var idle = Art(rect, "normal-glow", 402, 812, 0, 0);
            hover.Idle = idle.gameObject.AddComponent<CanvasGroup>();
            var highlight = Art(rect, "normal-hover", 402, 812, 0, 0);
            hover.Glow = highlight.gameObject.AddComponent<CanvasGroup>();
        }
        hover.Glow.alpha = 0;
        Art(rect, seasonal ? "seasonal-card" : "normal-card", 390, 800, 0, 0);
        Art(rect, character.Side == "Bear" ? "bear" : "usec", 386, 602, 0, -8).color = new Color(1, 1, 1, .078431f);
        var preview = UiElements.Rect("CharacterPreview", rect, 386, 740, 0, -5);
        hover.Model = preview.gameObject.AddComponent<CanvasGroup>();
        var raw = preview.gameObject.AddComponent<RawImage>();
        raw.color = Color.clear;
        raw.raycastTarget = false;
        if (character.Exists)
        {
            model(mode, raw);
        }
        else
        {
            Art(preview, seasonal ? "seasonal-empty" : "normal-empty", 350, 690, 0, 0);
        }

        var tint = seasonal ? new Color(.392f, .855f, .655f) : new Color(.482f, .639f, .667f);
        Art(rect, seasonal ? "seasonal-badge" : "normal-badge", 92, 92, -150, 355);
        _ui.Label(rect, "Mode", seasonal ? "PvE Season" : "PvE Zone", 36, 286, 47, 28, 370).color = tint;
        _ui.Label(rect, "Type", seasonal ? "SEASONAL" : "REGULAR", 16, 286, 26, 28, 336).color = seasonal
            ? new Color(tint.r, tint.g, tint.b, .6f)
            : UiElements.Muted;
        Art(rect, "footer-gradient", 390, 104, 0, -348);
        var info = UiElements.Rect("CharacterInfo", rect, 390, 190, 0, -150);
        hover.Info = info;
        var infoBackground = Art(info, "info-gradient", 390, 270, 0, 24);
        infoBackground.type = Image.Type.Sliced;
        infoBackground.rectTransform.pivot = new Vector2(.5f, 1);
        hover.InfoBackground = infoBackground.rectTransform;
        Art(info, "profile-icon", 20, 20, -170, -4);
        _ui.Label(
            info,
            "Nickname",
            character.Exists ? character.Name + " | " + character.Level : "NEW CHARACTER",
            24,
            328,
            38,
            15,
            -4
        ).color = new Color(.72f, .77f, .78f);
        var description = _ui.Label(
            info,
            "Description",
            seasonal
                ? "• Temporary seasonal character in Tarkov\n• Progress resets each season"
                : "• Your main character in Tarkov\n• Fight against AI opponents\n• Progress does not reset\n• Partial sync with EFT: Arena",
            16,
            350,
            100,
            0,
            -76
        );
        description.alignment = TextAnchor.UpperLeft;
        description.lineSpacing = 1.44f;
        description.rectTransform.pivot = new Vector2(.5f, 1);
        description.rectTransform.anchoredPosition = new Vector2(0, -26);
        description.rectTransform.sizeDelta = new Vector2(350, Mathf.Max(100, description.preferredHeight));
        description.color = new Color(.584f, .62f, .639f, .6f);
        hover.Description = description;
        if (seasonal)
        {
            Art(info, "season-banner", 386, 92, 0, -143);
            var details = UiElements.Rect("SeasonDetails", info, 390, 420, 0, -348);
            hover.Details = details.gameObject.AddComponent<CanvasGroup>();
            hover.Details.alpha = 0;
            var rules = state.Perks.Where(perk => perk.Common).ToArray();
            for (var i = 0; i < rules.Length; i++)
            {
                var px = i % 2 == 0 ? -95 : 95;
                var py = 153 - i / 2 * 34;
                var icon = UiElements.Fill(UiElements.Rect("PerkIcon-" + rules[i].Id, details, 24, 24, px - 69, py), Color.clear);
                // The caller resolves perk icons separately from decorative artwork.
                _artwork("perk:" + rules[i].Id, icon);
                _ui.Label(details, "PerkName", rules[i].Name, 16, 150, 28, px + 24, py).color = new Color(.392f, .855f, .655f, .6f);
            }
            Art(details, "season-stats-divider", 389, 12, 0, 47);
            _ui.Label(details, "StatsTitle", "SEASON STATS", 18, 354, 24, 0, 9).color = new Color(.851f, .851f, .851f);
            var captions = new[] { "Battle Pass rewards", "Story Chapters", "K/D", "Survivals" };
            var artwork = new[] { "season-stat-rewards", "season-stat-story", "season-stat-kd", "season-stat-survivals" };
            var values = new[] { character.BattlePassRewards, character.StoryChapters, character.Kd, character.Survivals };
            for (var i = 0; i < captions.Length; i++)
            {
                var row = UiElements.Rect("SeasonStat-" + i, details, 354, 24, 0, -29 - i * 32);
                Art(row, artwork[i], 24, 24, -165, 0);
                _ui.Label(row, "Caption", captions[i], 16, 238, 24, -28).color = new Color(.584f, .620f, .639f);
                var value = _ui.Label(row, "Value", values[i], 16, 72, 24, 141);
                value.alignment = TextAnchor.MiddleRight;
                value.color = new Color(.584f, .620f, .639f);
            }
            var infoButton = _ui.Button(rect, "", 28, 168, 369, seasonIntro, 28);
            infoButton.name = "SeasonInformation";
            infoButton.targetGraphic.color = Color.clear;
            Art(infoButton.transform, "info-icon", 24, 24, 0, 0);
        }
        var button = _ui.Button(
            rect,
            character.Exists ? "SELECT" : "CREATE",
            386,
            0,
            -356,
            () =>
            {
                if (character.Exists)
                {
                    select(mode);
                }
                else
                {
                    edit();
                }
            },
            80
        );
        button.name = "Select-" + mode;
        button.targetGraphic.color = Color.clear;
        button.GetComponentInChildren<Text>().fontSize = 24;
        hover.Apply(0);
    }
}
