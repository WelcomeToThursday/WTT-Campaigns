using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using WTT.Campaigns.UI.Controls;
using WTT.Campaigns.UI.Models;

namespace WTT.Campaigns.UI.Screens;

public sealed partial class CampaignScreen
{
    private bool _creating;
    public string SeasonId
    {
        get { return _state.SeasonId; }
    }
    public string CreationOperationId { get; private set; } = "";
    public string CreationCharacterId { get; private set; } = "";
    public Action<string>? SeasonChosen;
    public Action<string, string>? RecreationRequested;
    public Action<string, bool>? CharacterManagementRequested;

    public void BeginCreation(ScreenState state, string characterId = "")
    {
        _creating = true;
        CreationCharacterId = characterId;
        CreationOperationId = Guid.NewGuid().ToString("N");
        _creationDraft.Nickname = _name = "Campaign";
        _creationDraft.Side = _creationDraft.HeadId = _creationDraft.VoiceId = "";
        _creationDraft.Appearance = false;
        state.Selected = Array.Empty<string>();
        SetState(state, ScreenPage.CreationIdentity);
    }

    private void ChooseSeason() => ChooseContent(_state.Seasons, id => SeasonChosen?.Invoke(id), false);

    public void ShowTestDrafts(SeasonEntry[] drafts, Action<string> selected) => ChooseContent(drafts, selected, true);

    private void ChooseContent(SeasonEntry[] entries, Action<string> selected, bool testing)
    {
        if (_busy || DialogOpen)
        {
            return;
        }

        var choice = entries.FirstOrDefault(s => s.Id == _state.SeasonId) ?? entries.FirstOrDefault();
        var window = ConfirmationWindow("SeasonSelection", testing ? "Test a saved draft" : "Choose a campaign");
        var description = _ui.Label(
            window,
            "SeasonDescription",
            testing
                ? "Continue a separate test character. Progress is kept until you reset it; saved edits apply when you choose."
                : "Your new character will have separate equipment, progression and rewards in this campaign.",
            18,
            936,
            59,
            0,
            204
        );
        description.alignment = TextAnchor.MiddleCenter;
        description.color = new Color32(197, 195, 178, 255);

        var list = _ui.Scroll(window, "AvailableSeasons", 936, 322, 0, -19);
        list.GetComponent<Image>().color = Color.clear;
        ConfirmationBorder(list.transform, "Border", new Color32(88, 93, 96, 51));
        UiElements.Stretch(list.viewport, 0, 14);
        var layout = list.content.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(0, 0, 12, 12);
        layout.spacing = 6;
        list.verticalScrollbar.GetComponent<Image>().color = new Color32(88, 93, 96, 100);
        list.verticalScrollbar.targetGraphic.color = new Color32(197, 195, 178, 255);

        void RefreshSelection()
        {
            foreach (Transform row in list.content)
            {
                var selected = row.name == choice?.Id;
                row.GetComponent<Image>().color = selected ? new Color32(35, 38, 37, 255) : new Color32(12, 14, 14, 255);
                row.Find("SelectedBar").gameObject.SetActive(selected);
                row.Find("Selected").gameObject.SetActive(selected);
            }
        }

        foreach (var season in entries)
        {
            var button = _ui.Button(
                list.content,
                season.Name,
                922,
                0,
                0,
                () =>
                {
                    choice = season;
                    RefreshSelection();
                },
                80
            );
            button.name = season.Id;
            var label = button.GetComponentInChildren<Text>();
            label.fontSize = 18;
            label.fontStyle = FontStyle.Bold;
            label.alignment = TextAnchor.MiddleLeft;
            label.color = new Color32(197, 195, 178, 255);
            var hasDescription = !string.IsNullOrWhiteSpace(season.Description);
            var detail = _ui.Label(button.transform, "Description", season.Description, 15, 874, 42, 0, -15);
            detail.color = new Color32(149, 158, 163, 178);
            detail.alignment = TextAnchor.UpperLeft;
            detail.gameObject.SetActive(hasDescription);
            var detailHeight = hasDescription ? Mathf.Max(24, detail.preferredHeight) : 0;
            var height = hasDescription ? Mathf.Max(80, detailHeight + 52) : 62;
            button.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
            Place(label.rectTransform, 724, 28, -75, hasDescription ? height / 2 - 25 : 0);
            Place(detail.rectTransform, 874, detailHeight, 0, height / 2 - 44 - detailHeight / 2);
            UiElements.Fill(UiElements.Rect("SelectedBar", button.transform, 2, height - 16, -459), new Color32(197, 195, 178, 255));
            var selectedLabel = _ui.Label(button.transform, "Selected", "SELECTED", 14, 124, 28, 375, hasDescription ? height / 2 - 25 : 0);
            selectedLabel.alignment = TextAnchor.MiddleRight;
            selectedLabel.color = new Color32(197, 195, 178, 255);
        }
        RefreshSelection();
        if (choice == null)
        {
            var empty = _ui.Label(
                list.transform,
                "NoSeasons",
                testing
                    ? "Save an active campaign draft in Creator to test it here."
                    : "No playable campaigns are installed on this server.",
                18,
                872,
                100
            );
            empty.alignment = TextAnchor.MiddleCenter;
            empty.color = new Color32(149, 158, 163, 255);
        }
        ConfirmationActions(
            window,
            () =>
            {
                if (choice != null)
                {
                    selected(choice.Id);
                }
            },
            testing ? "CONTINUE TEST" : "CONTINUE",
            choice != null
        );
        LayoutRebuilder.ForceRebuildLayoutImmediate(list.content);
        list.verticalNormalizedPosition = 1;
        list.verticalScrollbar.SetValueWithoutNotify(1);
        list.verticalScrollbar.gameObject.SetActive(list.content.rect.height > list.viewport.rect.height);
    }

    private void ManageCharacter(CharacterEntry character, bool wipe)
    {
        if (_busy || DialogOpen || (!character.Exists && !character.Wiped) || character.Mode != "seasonal")
        {
            return;
        }

        var window = ConfirmationWindow("CharacterManagement", wipe ? "Wipe campaign character" : "Delete campaign character");
        var heading = _ui.Label(window, "Character", character.Name + "  /  " + character.SeasonName, 22, 936, 48, 0, 207);
        heading.alignment = TextAnchor.MiddleCenter;
        heading.color = new Color32(197, 195, 178, 255);

        var body = UiElements.Rect("ConsequencesPanel", window, 936, 322, 0, -19);
        ConfirmationBorder(body, "Border", new Color32(88, 93, 96, 51));
        var message = wipe
            ? "All items, equipment, currency, completed quests, leveled skills and campaign progress will be permanently reset. Earned in-game achievements will be kept.\n\nChoose your faction, appearance, voice and modifiers again. Your character will start this same campaign again with the bonuses and equipment from your current game edition.\n\nYour other characters are unaffected."
            : "Permanently delete this campaign character and its profile?\n\nAll items, equipment, currency, quests, skills, achievements and campaign progress belonging to this character will be lost.\n\nYour other characters are unaffected.";
        var description = _ui.Label(body, "Consequences", message, 18, 872, 230, 0, 20);
        description.alignment = TextAnchor.MiddleLeft;
        description.color = new Color32(149, 158, 163, 255);
        var warning = _ui.Label(body, "Irreversible", "This action cannot be undone.", 18, 872, 30, 0, -124);
        warning.alignment = TextAnchor.MiddleCenter;
        warning.color = new Color32(161, 72, 75, 255);
        ConfirmationActions(window, () => CharacterManagementRequested?.Invoke(character.Id, wipe));
    }
}
