using System;
using System.Collections.Generic;
using System.Linq;
using EFT;
using EFT.Quests;
using EFT.Trading;
using EFT.UI;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SeasonalPerks.Client.Progression;

/// <summary>Groups native rows without replacing quest actions, binding, or the detail panel.</summary>
internal sealed class GroupedTaskList : MonoBehaviour
{
    private static readonly string[] Roman = { "", "I", "II", "III", "IV" };
    private static readonly int[] Order = { 1, 2, 3, 4, 0, 5 };
    private readonly Dictionary<int, (GameObject Root, TMP_Text Label)> _headers = new();
    private readonly Dictionary<int, bool> _collapsed = new();
    private QuestsListView? _list;
    private QuestController? _controller;
    private Profile? _profile;
    private string _trader = "";
    private bool _busy;
    private float _nextCheck;
    private string _lastProgress = "";

    internal void Initialize(QuestsListView list, QuestController controller, Profile profile, Trader trader)
    {
        Clear();
        _list = list;
        _controller = controller;
        _profile = profile;
        _trader = trader.Id;
        foreach (var tier in Order)
            _collapsed[tier] = PlayerPrefs.GetInt(Key(tier), 0) == 1;
        Refresh();
    }

    internal void Clear()
    {
        foreach (var header in _headers.Values)
        {
            if (header.Root != null)
            {
                header.Root.SetActive(false);
                Destroy(header.Root);
            }
        }
        _headers.Clear();
        _collapsed.Clear();
        _list = null;
        _profile = null;
        _controller = null;
        _lastProgress = "";
    }

    private string Key(int tier) => "wtt_progression_collapsed_" + _trader + "_" + tier;

    private int Category(QuestListItem row) => row.Quest is DailyQuest ? 5 : ProgressionClient.Tier(row.Quest.Id);

    private bool Visible(QuestListItem row)
    {
        var state = row.Quest.QuestStatus;
        return row.Quest.IsVisible
            && (_list!._toggleShowLocked.isOn || state != EQuestStatus.Locked)
            && (_list._toggleShowCompleted.isOn || state is not (EQuestStatus.Success or EQuestStatus.Fail));
    }

    internal void Refresh()
    {
        if (_list == null || _busy)
            return;
        _busy = true;
        try
        {
            var rows = _list
                ._questListContainer.GetComponentsInChildren<QuestListItem>(true)
                .Where(r => r.Quest != null)
                .OrderBy(r => r.transform.GetSiblingIndex())
                .ToArray();
            var index = 0;
            foreach (var tier in Order)
            {
                var members = rows.Where(r => Category(r) == tier).ToArray();
                if (!_headers.TryGetValue(tier, out var header))
                {
                    header = CreateHeader(tier);
                    _headers[tier] = header;
                }
                var any = members.Any(Visible);
                header.Root.SetActive(any);
                header.Root.transform.SetSiblingIndex(index++);
                var label =
                    tier is >= 1 and <= 4 ? "<b>" + Roman[tier] + "</b>  " + "LOYALTY LEVEL".Localized() + " " + tier
                    : tier == 0 ? "ESSENTIAL TASKS".Localized()
                    : "REPEATABLE TASKS".Localized();
                header.Label.text = label;
                header.Root.transform.Find("Chevron").localEulerAngles = new Vector3(0, 0, _collapsed[tier] ? 0 : 180);
                foreach (var row in members)
                {
                    row.transform.SetSiblingIndex(index++);
                    row.gameObject.SetActive(any && !_collapsed[tier] && Visible(row));
                }
            }
            _list.AutoSelect();
        }
        finally
        {
            _busy = false;
        }
    }

    private (GameObject, TMP_Text) CreateHeader(int tier)
    {
        var root = new GameObject(
            "Loyalty task group " + tier,
            typeof(RectTransform),
            typeof(Image),
            typeof(Button),
            typeof(LayoutElement)
        );
        root.transform.SetParent(_list!._questListContainer, false);
        root.GetComponent<LayoutElement>().preferredHeight = 38;
        root.GetComponent<LayoutElement>().minHeight = 38;
        var background = root.GetComponent<Image>();
        background.color = new Color(0.015f, 0.035f, 0.032f, 0.95f);
        var button = root.GetComponent<Button>();
        button.targetGraphic = background;
        button.onClick.AddListener(() =>
        {
            _collapsed[tier] = !_collapsed[tier];
            PlayerPrefs.SetInt(Key(tier), _collapsed[tier] ? 1 : 0);
            Refresh();
        });
        var text = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        text.transform.SetParent(root.transform, false);
        var rect = text.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(10, 0);
        rect.offsetMax = new Vector2(-35, 0);
        var source = _list._questListItemPrefab._title;
        text.font = source.font;
        text.fontSharedMaterial = source.fontSharedMaterial;
        text.fontSize = 17;
        text.color = new Color(0.66f, 0.72f, 0.7f, 1);
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.raycastTarget = false;
        var arrow = new GameObject("Chevron", typeof(RectTransform)).GetComponent<RectTransform>();
        arrow.SetParent(root.transform, false);
        arrow.anchorMin = arrow.anchorMax = new Vector2(1, 0.5f);
        arrow.sizeDelta = new Vector2(16, 12);
        arrow.anchoredPosition = new Vector2(-18, 0);
        for (var side = -1; side <= 1; side += 2)
        {
            var stroke = new GameObject("Stroke", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            stroke.transform.SetParent(arrow, false);
            stroke.rectTransform.sizeDelta = new Vector2(8, 2);
            stroke.rectTransform.anchoredPosition = new Vector2(side * 2.5f, 0);
            stroke.rectTransform.localEulerAngles = new Vector3(0, 0, side * 45);
            stroke.color = text.color;
            stroke.raycastTarget = false;
        }
        return (root, text);
    }

    internal bool AutoSelect()
    {
        if (_list == null)
            return false;
        var row = _list
            ._questListContainer.GetComponentsInChildren<QuestListItem>(false)
            .Where(r => r.Quest != null && r.gameObject.activeSelf)
            .OrderBy(r => r.transform.GetSiblingIndex())
            .FirstOrDefault();
        if (row == null)
            return false; // Native clears the detail panel when no visible row remains.
        _list.OnQuestSelected(row);
        return true;
    }

    private void Update()
    {
        if (_profile == null || _controller == null || _list == null || Time.unscaledTime < _nextCheck)
            return;
        _nextCheck = Time.unscaledTime + 0.25f;
        var progress =
            _profile.Info.Level
            + ":"
            + string.Join(
                ";",
                _profile.TradersInfo.Select(t => t.Key + ":" + t.Value.LoyaltyLevel + ":" + t.Value.Standing + ":" + t.Value.Available)
            );
        if (progress == _lastProgress)
            return;
        _lastProgress = progress;
        foreach (var quest in _controller.Quests.ToArray())
        {
            if (
                ProgressionClient.Metadata?.Quests.ContainsKey(quest.Id) == true
                && quest.QuestStatus is EQuestStatus.Locked or EQuestStatus.AvailableForStart
            )
                quest.CheckForStatusChange(EQuestStatus.AvailableForStart, notify: true, fromServer: false, canFail: false);
        }
        _list.UpdateVisibility();
    }

    private void OnDisable() => Clear();
}
