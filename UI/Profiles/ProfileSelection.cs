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
        Action<bool>? hoverSound = null,
        Action<CharacterEntry, bool>? manage = null,
        Func<bool>? canNavigate = null,
        Action<CharacterEntry>? recreate = null
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
        var viewport = UiElements.Rect("ProfileCarousel", root, 1720, 1020, 0, -10);
        UiElements.Fill(viewport, Color.clear, true);
        viewport.gameObject.AddComponent<RectMask2D>();
        var carousel = viewport.gameObject.AddComponent<ProfileCarousel>();
        carousel.CanNavigate = canNavigate;
        var characters = state
            .Characters.Where(c => c.Exists || c.Wiped)
            .Concat(new[] { new CharacterEntry { Mode = "seasonal" } })
            .ToArray();
        foreach (var character in characters)
        {
            carousel.Add(Card(viewport, state, character, model, select, edit, seasonIntro, hoverSound, manage, recreate));
        }

        var previous = _ui.Button(root, "‹", 70, -885, -10, () => carousel.Move(-1), 90);
        var next = _ui.Button(root, "›", 70, 885, -10, () => carousel.Move(1), 90);
        previous.name = "PreviousProfile";
        next.name = "NextProfile";
        previous.targetGraphic.color = next.targetGraphic.color = Color.clear;
        previous.GetComponentInChildren<Text>().fontSize = next.GetComponentInChildren<Text>().fontSize = 52;
        carousel.Counter = _ui.Label(root, "ProfileCount", "", 18, 150, 30, 0, -477);
        carousel.Counter.alignment = TextAnchor.MiddleCenter;
        var hint = _ui.Label(root, "CarouselHint", "SCROLL  /  DRAG  /  LEFT & RIGHT     TO BROWSE CHARACTERS", 16, 950, 30, 0, -508);
        hint.alignment = TextAnchor.MiddleCenter;
        hint.color = UiElements.Muted;
        var create = _ui.Button(root, "+ NEW SEASONAL CHARACTER", 340, 0, -441, edit, 38);
        create.targetGraphic.color = Color.clear;
        create.GetComponentInChildren<Text>().color = new Color(.392f, .855f, .655f);
        carousel.Focus(
            Array.FindIndex(characters, c => c.Id == (state.ActiveMode == "seasonal" ? state.SelectedCharacterId : characters[0].Id))
        );
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
        // Backgrounds follow the viewport; cards retain their native proportions.
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

    private RectTransform Card(
        Transform parent,
        ScreenState state,
        CharacterEntry character,
        Action<string, RawImage> model,
        Action<string> select,
        Action edit,
        Action seasonIntro,
        Action<bool>? hoverSound,
        Action<CharacterEntry, bool>? manage,
        Action<CharacterEntry>? recreate
    )
    {
        var mode = character.Mode;
        var seasonal = mode == "seasonal";
        var rect = UiElements.Rect((character.Exists || character.Wiped ? character.Id : "new-seasonal") + "-profile", parent, 390, 800);
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
        if (character.Exists)
        {
            var loader = rect.gameObject.AddComponent<ProfileCardPreview>();
            loader.Host = preview;
            loader.Load = raw => model(character.Id.Length > 0 ? character.Id : mode, raw);
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
        // Live reveals the sliding information through the card, rather than drawing
        // the off-card portion of the panel while it moves into place. Keep the
        // oversized glow layers outside this mask so their intended spill is retained.
        var contentMask = UiElements.Rect("CardContentMask", rect, 390, 800);
        contentMask.gameObject.AddComponent<RectMask2D>();
        var info = UiElements.Rect("CharacterInfo", contentMask, 390, 190, 0, -150);
        hover.Info = info;
        var infoBackground = Art(info, "info-gradient", 390, 270, 0, 24);
        infoBackground.type = Image.Type.Sliced;
        infoBackground.rectTransform.pivot = new Vector2(.5f, 1);
        hover.InfoBackground = infoBackground.rectTransform;
        Art(info, "profile-icon", 20, 20, -170, -4);
        _ui.Label(
            info,
            "Nickname",
            character.Exists ? character.Name + " | " + character.Level
                : character.Wiped ? "CHARACTER WIPED"
                : "NEW CHARACTER",
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
                ? (
                    character.Exists ? "• Separate seasonal progression\n• " + character.SeasonName
                    : character.Wiped ? "• Earned achievements kept\n• Choose your character again"
                    : "• Create another seasonal character\n• Choose a season and personal modifiers"
                )
                : "• Your main character in Tarkov\n• Fight against AI opponents\n• Progress does not reset\n• Independent equipment and progression",
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
            var seasonLabel = _ui.Label(
                info,
                "SeasonName",
                character.Exists || character.Wiped ? character.SeasonName.ToUpperInvariant() : "CHOOSE YOUR SEASON",
                19,
                350,
                34,
                0,
                -95
            );
            seasonLabel.color = tint;
            seasonLabel.alignment = TextAnchor.MiddleCenter;
            var details = UiElements.Rect("SeasonDetails", info, 390, 420, 0, -348);
            hover.Details = details.gameObject.AddComponent<CanvasGroup>();
            hover.Details.alpha = 0;
            var rules = state.Perks.Where(perk => perk.Common && character.SeasonId == state.SeasonId).Take(6).ToArray();
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
            character.Exists ? "SELECT"
                : character.Wiped ? "RECREATE"
                : "CREATE",
            386,
            0,
            -356,
            () =>
            {
                if (character.Exists)
                {
                    select(character.Id.Length > 0 ? character.Id : mode);
                }
                else if (character.Wiped)
                {
                    recreate?.Invoke(character);
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
        button.interactable = character.Available;
        if (character.Exists && !character.Available)
        {
            button.GetComponentInChildren<Text>().text = "SEASON UNAVAILABLE";
        }

        if (seasonal && (character.Exists || character.Wiped) && manage != null)
        {
            var wipe = _ui.Button(rect, "WIPE", 170, -94, -288, () => manage(character, true), 40);
            var delete = _ui.Button(rect, "DELETE", 170, 94, -288, () => manage(character, false), 40);
            wipe.targetGraphic.color = delete.targetGraphic.color = Color.clear;
            wipe.GetComponentInChildren<Text>().fontSize = delete.GetComponentInChildren<Text>().fontSize = 24;
            wipe.gameObject.SetActive(!character.Wiped);
            wipe.interactable = character.Available;
        }
        hover.Apply(0);
        return rect;
    }
}
