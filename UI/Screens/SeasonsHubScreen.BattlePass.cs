using System;
using System.Linq;
using SeasonalPerks.UI.BattlePass;
using SeasonalPerks.UI.Controls;
using SeasonalPerks.UI.Models;
using UnityEngine;
using UnityEngine.UI;

namespace SeasonalPerks.UI.Screens;

public sealed partial class SeasonsHubScreen
{
    private void RenderBattlePass(Transform root)
    {
        Art(root, "SeasonBadge", "sharedassets44-499", 115, 58, 48, 48).preserveAspect = true;
        Caption(root, "SeasonLabel", "SEASON\nONE", 15, 177, 59, 75, 47);
        Art(root, "RewardListBackground", "sharedassets48-475", 108, 150, 416, 874);
        Art(root, "RewardBackdrop", "sharedassets48-492", 540, 150, 840, 770);
        Art(root, "RequirementsBackground", "sharedassets48-463", 1396, 150, 416, 770);
        Caption(root, "RewardListTitle", "REWARD LIST", 23, 122, 160, 370, 45);
        if (_state.Pages.Length == 0)
        {
            Caption(root, "Empty", "No Battle Pass rewards are available.", 22, 560, 450, 800, 100);
            return;
        }
        var page = _state.Pages[PageIndex];
        var grid = Box(root, "BattlePassRewardGrid", 108, 226, 416, 634);
        UiElements.Fill(grid, Color.clear, true);
        grid.gameObject.AddComponent<HubPointer>().Scroll = ChangePage;
        for (var i = 0; i < page.Rewards.Length; i++)
        {
            var index = i;
            RewardTile(grid, page.Rewards[i], i == SelectedRewardIndex, 204, 206, () => SelectReward(index));
        }
        var markerWidth = 400f / _state.Pages.Length;
        for (var i = 0; i < _state.Pages.Length; i++)
        {
            var marker = UiElements.Fill(
                Box(root, "PageMarker" + i, 116 + i * markerWidth, 933, markerWidth - 4, 4),
                i == PageIndex ? new Color(.43f, .7f, .62f) : new Color(.18f, .25f, .24f)
            );
            marker.raycastTarget = false;
        }
        Pager(root, PageIndex, _state.Pages.Length, 120, 964, 388);
        var selected = page.Rewards.ElementAtOrDefault(SelectedRewardIndex);
        if (selected != null)
        {
            Remote(root, "RewardFullImage", selected.BigImage, 554, 202, 812, 620);
            var number = _state.Pages.Take(PageIndex).Sum(p => p.Rewards.Length) + SelectedRewardIndex + 1;
            Caption(root, "RewardItemName", "ITEM    OVERVIEW-BP-" + number.ToString("D3"), 14, 555, 853, 800, 25);
            Caption(
                root,
                "RewardClass",
                "CLASS    " + selected.Kind + (selected.Side.Length > 0 ? "    " + selected.Side : ""),
                14,
                555,
                879,
                800,
                25
            );
            Caption(root, "RewardName", selected.Name.ToUpperInvariant(), 22, 1412, 160, 386, 58);
            var top = 234f;
            if (page.PreviousRequirement > 0)
            {
                Caption(
                    root,
                    "PreviousPageRequirement",
                    "□  Claim rewards from page " + PageIndex + "          0/" + page.PreviousRequirement,
                    18,
                    1412,
                    top,
                    380,
                    58
                );
                top += 68;
            }
            Caption(root, "CollectRequirement", "□  Collect and submit documents", 18, 1412, top, 380, 38);
            top += 48;
            for (var i = 0; i < selected.Costs.Length; i++)
            {
                var cost = selected.Costs[i];
                var document = _state.Documents.FirstOrDefault(d => d.Id == cost.DocumentId);
                if (document == null)
                {
                    continue;
                }

                var x = 1412 + i % 5 * 74;
                var y = top + i / 5 * 78;
                var image = Remote(
                    root,
                    "RequirementDocument" + i,
                    document.Count >= cost.Count ? document.Image : document.UnavailableImage,
                    x,
                    y,
                    68,
                    68
                );
                Hint(image.gameObject, document.Name, x - 100, y - 85);
                Caption(root, "DocumentCost" + i, document.Count + "/" + cost.Count, 16, x + 25, y + 43, 42, 25).alignment =
                    TextAnchor.MiddleRight;
            }
            top += Math.Max(1, (selected.Costs.Length + 4) / 5) * 78;
            DisabledAction(root, selected.Claimed ? "CLAIMED" : "CLAIM REWARD", "sharedassets48-545", 1412, top, 380);
            Caption(
                root,
                "ClaimHelp",
                "You will receive the reward after\nsubmitting the documents",
                16,
                1440,
                top + 43,
                324,
                60
            ).alignment = TextAnchor.MiddleCenter;
        }
        Caption(root, "DocumentsLabel", "ⓘ  Documents", 17, 554, 930, 350, 28);
        Caption(
            root,
            "DocumentLimit",
            "Available limit:  " + _state.DocumentLimit + "/" + _state.DocumentLimit,
            15,
            1080,
            930,
            290,
            28
        ).alignment = TextAnchor.MiddleRight;
        for (var i = 0; i < _state.Documents.Length; i++)
        {
            var doc = _state.Documents[i];
            var x = 554 + i * 82;
            var im = Remote(root, "InventoryDocument" + i, doc.Count == 0 ? doc.UnavailableImage : doc.Image, x, 968, 78, 78);
            Hint(im.gameObject, doc.Name + "\nOwned: " + doc.Count, x, 870);
            Caption(root, "DocumentCount" + i, "x" + doc.Count, 16, x + 35, 1016, 40, 24).alignment = TextAnchor.MiddleRight;
        }
        Art(root, "UniversalBackground", "sharedassets48-487", 1214, 968, 154, 78);
        var universal = Remote(
            root,
            "UniversalDocuments",
            _state.UniversalCount == 0 ? _state.UniversalUnavailableImage : _state.UniversalImage,
            1214,
            968,
            154,
            78
        );
        Hint(universal.gameObject, "Universal documents\nNot available yet.", 1110, 870);
        Caption(root, "UniversalCount", "x" + _state.UniversalCount, 16, 1318, 1016, 40, 24);
        DisabledAction(root, "BUY DOCUMENTS", "sharedassets48-537", 1396, 966, 416);
        DisabledAction(root, "EXCHANGE DOCUMENTS", "sharedassets48-526", 1396, 1009, 416);
    }

    private void RewardTile(Transform parent, HubReward reward, bool selected, float cellWidth, float cellHeight, Action select)
    {
        var width = cellWidth * reward.Width + 8 * (reward.Width - 1);
        var height = cellHeight * reward.Height + 8 * (reward.Height - 1);
        var rect = Box(parent, "Reward-" + reward.Id, reward.X * (cellWidth + 8), reward.Y * (cellHeight + 8), width, height);
        var bg = UiElements.Fill(rect, selected ? new Color(.07f, .13f, .115f, .7f) : new Color(.045f, .055f, .054f, .7f), true);
        bg.sprite = _art("sharedassets48-296");
        var image = Remote(rect, "Thumbnail", reward.Image, 2, 2, width - 4, height - 4);
        image.raycastTarget = false;
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = bg;
        var colors = button.colors;
        colors.highlightedColor = new Color(1.35f, 1.5f, 1.4f);
        button.colors = colors;
        _ui.Feedback(button);
        button.onClick.AddListener(() => select());
        if (selected)
        {
            var frame = Art(rect, "SelectedFrame", "resources-5972", 0, 0, width, height);
            frame.type = Image.Type.Sliced;
            frame.color = new Color(.38f, .66f, .57f, .75f);
        }
        Art(rect, "State", reward.Claimed ? "sharedassets48-448" : "sharedassets48-375", width - 24, height - 24, 14, 14).preserveAspect =
            true;
        if (reward.Side.Length > 0)
        {
            Caption(rect, "Faction", reward.Side, 12, 6, 5, width - 12, 25);
        }
    }

    private void Pager(Transform root, int index, int count, float x, float y, float width)
    {
        var previous = Button(root, "‹", x, y, 38, 36, () => ChangePage(-1));
        var next = Button(root, "›", x + width - 38, y, 38, 36, () => ChangePage(1));
        previous.interactable = index > 0;
        next.interactable = index + 1 < count;
        Caption(
            root,
            "PageNumber",
            (Tab == HubTab.BattlePass ? "PAGE " : "") + (index + 1) + "/" + count,
            18,
            x + 60,
            y,
            width - 120,
            36
        ).alignment = TextAnchor.MiddleCenter;
        Caption(root, "PreviousKey", "Q", 12, x + 43, y + 7, 18, 20);
        Caption(root, "NextKey", "E", 12, x + width - 57, y + 7, 18, 20);
    }
}
