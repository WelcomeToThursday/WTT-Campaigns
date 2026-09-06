using System;
using System.Linq;
using SeasonalPerks.UI.BattlePass;
using SeasonalPerks.UI.Controls;
using UnityEngine;
using UnityEngine.UI;

namespace SeasonalPerks.UI.Screens;

public sealed partial class SeasonsHubScreen
{
    private void RenderSeasonal(Transform root)
    {
        Art(root, "SeasonalRewardsBackground", "sharedassets48-467", 108, 385, 1200, 640);
        Caption(
            root,
            "SeasonalExplanation",
            "Seasonal rewards are available after completing special tasks",
            17,
            230,
            380,
            965,
            40
        ).alignment = TextAnchor.MiddleCenter;
        var grid = Box(root, "SeasonalRewardGrid", 224, 478, 970, 510);
        for (var i = 0; i < _state.SeasonalRewards.Length; i++)
        {
            var index = i;
            RewardTile(grid, _state.SeasonalRewards[i], i == _seasonalSelection, 184, 222, () => SelectReward(index));
        }
        var selected = _state.SeasonalRewards.ElementAtOrDefault(_seasonalSelection);
        if (selected == null)
        {
            return;
        }

        Art(root, "SeasonalRewardDetailsBackground", "sharedassets48-463", 1320, 385, 492, 640);
        Caption(root, "SeasonalRewardKind", selected.Kind, 13, 1336, 390, 456, 22);
        Caption(root, "SeasonalRewardName", selected.Name, 21, 1336, 414, 456, 55);
        Remote(root, "SeasonalFullImage", selected.BigImage, 1336, 470, 456, 350);
        var text = string.Join(
            "\n",
            selected.Requirements.Select(
                (r, i) =>
                {
                    var requirement = selected.Eligibility.ElementAtOrDefault(i);
                    return (requirement?.Met == true ? "✓  " : "□  ")
                        + r
                        + "  "
                        + (requirement?.Current ?? 0)
                        + "/"
                        + (requirement?.Required ?? 1);
                }
            )
        );
        Caption(root, "SeasonalRequirements", text, 16, 1336, 850, 456, 112);
        ClaimAction(root, selected, 1336, 982, 456);
        var info = Caption(root, "RewardInfo", "ⓘ", 22, 1757, 810, 35, 32);
        Hint(info.gameObject, selected.Description.Length > 0 ? selected.Description : selected.Name, 1340, 700);
    }

    private void RenderAbout(Transform root)
    {
        if (_state.Slides.Length > 0)
        {
            var slide = _state.Slides[Math.Min(SlideIndex, _state.Slides.Length - 1)];
            Art(root, "CarouselArtwork", slide.Image, 108, 390, 982, 581);
            var text = Caption(root, "CarouselText", slide.Text, 18, 183, 775, 832, 176);
            text.alignment = TextAnchor.LowerCenter;
            text.color = new Color(.53f, .78f, .68f);
            var carousel = UiElements.Fill(Box(root, "CarouselScrollArea", 108, 390, 982, 581), Color.clear, true);
            carousel.gameObject.AddComponent<HubPointer>().Scroll = ChangePage;
            Pager(root, SlideIndex, _state.Slides.Length, 526, 991, 170);
        }
        Art(root, "CommonPanel", "sharedassets48-362", 1108, 390, 704, 172);
        Caption(root, "CommonTitle", "COMMON MODIFIERS", 19, 1124, 396, 650, 40);
        var common = _perks.Where(p => p.Common).ToArray();
        for (var i = 0; i < common.Length; i++)
        {
            ModifierIcon(root, common[i], 1144 + i * 68, 475);
        }

        Caption(root, "PersonalTitle", "PERSONAL MODIFIERS", 19, 1124, 566, 650, 40);
        Art(root, "PositivePanel", "sharedassets48-447", 1108, 617, 352, 417).color = new Color(.26f, .43f, .27f, .8f);
        Art(root, "NegativePanel", "sharedassets48-447", 1460, 617, 352, 417).color = new Color(.49f, .17f, .21f, .8f);
        for (var group = 0; group < 2; group++)
        {
            var positive = group == 0;
            var perks = _perks.Where(p => !p.Common && (p.Points < 0) == positive).ToArray();
            for (var i = 0; i < perks.Length; i++)
            {
                ModifierIcon(root, perks[i], 1140 + group * 352 + i % 4 * 78, 645 + i / 4 * 77);
            }
        }
    }

    private void ModifierIcon(Transform root, Models.PerkEntry perk, float x, float y)
    {
        var icon = UiElements.Fill(Box(root, "Modifier-" + perk.Id, x, y, 50, 50), Color.clear, true);
        PerkIconRequested?.Invoke(perk.Id, icon);
        Hint(
            icon.gameObject,
            perk.Name + "\n" + perk.Description + (perk.Unavailable.Length > 0 ? "\n\n" + perk.Unavailable : ""),
            x - 220,
            y + 58
        );
    }
}
