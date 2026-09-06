using System;
using System.Collections.Generic;
using System.Linq;
using SeasonalPerks.UI.Audio;
using SeasonalPerks.UI.Controls;
using SeasonalPerks.UI.Creation;
using SeasonalPerks.UI.Models;
using SeasonalPerks.UI.Profiles;
using UnityEngine;
using UnityEngine.UI;

namespace SeasonalPerks.UI.Screens;

public sealed partial class SeasonalScreen : IDisposable
{
    private readonly UiElements _ui;
    private readonly Func<string, GameObject> _prefab;
    private readonly bool _embedded;
    private readonly RectTransform _panel;
    private readonly RectTransform _body;
    private readonly CanvasGroup _controls;
    private readonly Text _title;
    private readonly Text _subtitle;
    private readonly Text _status;
    private readonly Text _balance;
    private readonly Button _primary;
    private readonly Button _reset;
    private readonly Dictionary<ScreenPage, Button> _tabs = new Dictionary<ScreenPage, Button>();
    private readonly HashSet<string> _selected = new HashSet<string>();
    private readonly List<(PerkEntry Entry, GameObject Card)> _cards = new List<(PerkEntry, GameObject)>();
    private ScreenState _state = new ScreenState();
    private InputField? _search;
    private InputField? _nickname;
    private GameObject? _dialog;
    private bool _busy;
    private bool _disposed;
    private ProfileSelection? _profileSelection;
    private string _name = "Seasonal";
    private string _side = "Usec";
    private string _query = "";
    private readonly CreationDraft _creationDraft = new CreationDraft();
    private ICreationIdentity? _identity;
    private RectTransform? _creationBackground;
    private RectTransform? _creationGlow;
    private Button? _creationNext;
    private Button? _creationBack;
    private Text? _creationPoints;
    private Button? _creationReset;

    public GameObject Root { get; }
    public ScreenPage Page { get; private set; }
    public Action? CloseRequested;
    public Action? SaveRequested;
    public Action<string>? SwitchRequested;
    public Action<InterfaceSound>? SoundRequested;
    public Action<bool>? ProfileHoverSound;
    public Action? EditRequested;
    public Action<string, Image>? IconRequested;
    public Action<string, Image>? ArtworkRequested;
    public Action<string, RawImage>? CharacterRequested;
    public Func<Transform, CreationDraft, Action, Action, ICreationIdentity>? IdentityRequested;
    public bool StartupSelection;
    public Material? GlowMaterial;
    public string[] Selected
    {
        get { return _selected.OrderBy(id => id, StringComparer.Ordinal).ToArray(); }
    }

    public string Nickname
    {
        get { return _name; }
    }

    public string Side
    {
        get { return _side; }
    }

    public string HeadId
    {
        get { return _creationDraft.HeadId; }
    }

    public string VoiceId
    {
        get { return _creationDraft.VoiceId; }
    }

    private bool PersonalPage
    {
        get { return Page == ScreenPage.Personal || Page == ScreenPage.CreationPersonal; }
    }

    private bool CreationPage
    {
        get { return Page == ScreenPage.CreationIdentity || Page == ScreenPage.CreationCommon || Page == ScreenPage.CreationPersonal; }
    }

    public bool Dirty
    {
        get { return !_selected.SetEquals(_state.Selected); }
    }

    public bool DialogOpen
    {
        get { return _dialog != null; }
    }

    private bool Created
    {
        get { return _state.Characters.Any(character => character.Mode == "seasonal" && character.Exists); }
    }

    private int Remaining
    {
        get
        {
            return _state.StartingPoints + _state.Perks.Where(perk => !perk.Common && _selected.Contains(perk.Id)).Sum(perk => perk.Points);
        }
    }

    public SeasonalScreen(Transform parent, Func<string, GameObject> prefab, Font font, bool embedded = false)
    {
        _ui = new UiElements(font, sound => SoundRequested?.Invoke(sound));
        _prefab = prefab;
        _embedded = embedded;
        var root = UiElements.Rect("SeasonalPerksScreen", parent, 0, 0);
        Root = root.gameObject;
        UiElements.Stretch(root);
        UiElements.Fill(root, embedded ? Color.clear : new Color(.014f, .016f, .013f, .99f), true);
        _panel = UiElements.Rect("SafeArea", root, 1740, 940);
        _controls = _panel.gameObject.AddComponent<CanvasGroup>();
        _title = _ui.Label(_panel, "Title", "CHARACTER SELECTION", 32, 1150, 50, -250, 425);
        _subtitle = _ui.Label(_panel, "Subtitle", "", 17, 1350, 32, -150, 382);
        _subtitle.color = UiElements.Muted;
        if (!embedded)
        {
            _ui.Button(_panel, "BACK", 140, 790, 425, RequestClose);
            AddTab(ScreenPage.Characters, "CHARACTERS", -655);
            AddTab(ScreenPage.Personal, "PERSONAL PERKS", -355);
            AddTab(ScreenPage.Global, "GLOBAL RULES", -55);
        }
        UiElements.Fill(UiElements.Rect("HeaderLine", _panel, 1740, 1, 0, 294), new Color(.29f, .29f, .23f));
        _body = UiElements.Rect("Page", _panel, 1740, 610, 0, -26);
        _status = _ui.Label(_panel, "Status", "", 18, 1710, 46, -10, -365);
        _balance = _ui.Label(_panel, "SelectionSummary", "", 19, 1010, 50, -360, -425);
        _reset = _ui.Button(_panel, "RESET", 160, 460, -425, ResetSelection, clickSound: InterfaceSound.PerkReset);
        _primary = _ui.Button(_panel, "REVIEW SELECTION", 275, 722, -425, PrimaryAction);
        Root.SetActive(false);
    }

    private void AddTab(ScreenPage page, string caption, float x)
    {
        _tabs[page] = _ui.Button(_panel, caption, 280, x, 333, () => ShowPage(page));
    }

    public void SetState(ScreenState state, ScreenPage? page = null)
    {
        _state = state;
        _selected.Clear();
        foreach (var id in state.Selected)
        {
            _selected.Add(id);
        }
        DismissDialog();
        ShowPage(page ?? Page);
    }

    public void Open(ScreenPage page)
    {
        Root.SetActive(true);
        ShowPage(page);
        Fit();
    }

    public void Fit()
    {
        if (_disposed)
        {
            return;
        }
        var size = ((RectTransform)Root.transform).rect.size;
        if (_embedded)
        {
            var embeddedScale = size.x / 1920f;
            if (embeddedScale > 0)
            {
                _panel.localScale = Vector3.one * embeddedScale;
                _panel.sizeDelta = new Vector2(1920, size.y / embeddedScale);
            }
            return;
        }
        var scale =
            Page == ScreenPage.Characters || CreationPage
                ? Mathf.Min(size.x / 1920f, size.y / 1080f)
                : Mathf.Min(size.x / 1800f, size.y / 980f);
        if (scale > 0)
        {
            _panel.localScale = Vector3.one * scale;
            _profileSelection?.Fit(size / scale);
            FitSeasonIntroduction(size / scale);
            if (_creationBackground)
            {
                _creationBackground!.sizeDelta = size / scale;
                _creationGlow!.sizeDelta = new Vector2(size.x / scale, 512);
                _creationGlow.anchoredPosition = new Vector2(0, size.y / scale * .5f);
            }
        }
    }

    public void SetBusy(bool busy, string message = "")
    {
        _busy = busy;
        ClearCardHover();
        _controls.interactable = !busy;
        _controls.blocksRaycasts = true;
        if (busy)
        {
            DismissDialog();
        }
        RefreshFooter();
        SetMessage(message);
    }

    public void SetMessage(string message, bool error = false)
    {
        _status.text = message;
        _status.color = error ? UiElements.Negative : UiElements.Ink;
    }

    public void ShowPage(ScreenPage page)
    {
        if (_busy || _dialog != null)
        {
            return;
        }
        Page = page;
        var fullScreen = page == ScreenPage.Characters || CreationPage;
        foreach (Transform child in _panel)
        {
            child.gameObject.SetActive((_embedded || fullScreen) ? child == _body || child == _status.transform : true);
        }
        Place(_body, fullScreen ? 1920 : 1740, fullScreen ? 1080 : 610, 0, fullScreen ? 0 : -26);
        if (_embedded)
        {
            UiElements.Stretch(_body);
        }
        _status.rectTransform.anchoredPosition = new Vector2(-10, fullScreen ? -520 : -365);
        _query = "";
        _cards.Clear();
        _search = null;
        _nickname = null;
        _profileSelection = null;
        _identity?.Dispose();
        _identity = null;
        _creationBackground = null;
        _creationGlow = null;
        _creationNext = null;
        _creationBack = null;
        _creationPoints = null;
        _creationReset = null;
        foreach (var child in _body.Cast<Transform>().ToArray())
        {
            child.gameObject.SetActive(false);
            UiElements.Destroy(child.gameObject);
        }
        foreach (var tab in _tabs)
        {
            ((Image)tab.Value.targetGraphic).color = tab.Key == page ? new Color(.35f, .33f, .24f) : new Color(.12f, .13f, .11f);
        }
        _title.text =
            page == ScreenPage.Characters ? "CHARACTER SELECTION"
            : page == ScreenPage.Personal ? "PERSONAL PERKS"
            : page == ScreenPage.Global ? "GLOBAL MODIFIERS"
            : "SEASONAL PERKS";
        _subtitle.text =
            page == ScreenPage.Characters ? "Choose your character. Each has its own progression and equipment."
            : page == ScreenPage.Personal ? "Balance detrimental modifiers with beneficial perks. Review your selection before saving."
            : page == ScreenPage.Global ? "Season-wide rules are configured on the SPT server and apply to your seasonal PMC."
            : _state.IsScav ? "Seasonal PMC perks do not apply to your Scav."
            : _state.ActiveMode == "seasonal" ? "Perks currently applied to this seasonal character."
            : "Your normal character has no seasonal modifiers.";
        if (_embedded)
        {
            BuildModifiers();
        }
        else if (page == ScreenPage.Characters)
        {
            _profileSelection = new ProfileSelection(
                _body,
                _ui.Font,
                _state,
                (name, image) =>
                {
                    if (name.StartsWith("perk:", StringComparison.Ordinal))
                    {
                        IconRequested?.Invoke(name.Substring(5), image);
                    }
                    else
                    {
                        ArtworkRequested?.Invoke(name, image);
                    }
                },
                (mode, target) => CharacterRequested?.Invoke(mode, target),
                mode => WithDiscardConfirmation(() => SwitchRequested?.Invoke(mode)),
                () => ShowPage(Created ? ScreenPage.Personal : ScreenPage.CreationIdentity),
                ShowSeasonIntroduction,
                RequestClose,
                StartupSelection,
                GlowMaterial,
                sound => SoundRequested?.Invoke(sound),
                seasonal =>
                {
                    if (!_busy)
                    {
                        ProfileHoverSound?.Invoke(seasonal);
                    }
                }
            );
        }
        else if (CreationPage)
        {
            BuildCreation();
        }
        else
        {
            BuildPerks();
        }
        SetMessage("");
        RefreshFooter();
        Fit();
    }

    private void RefreshFooter()
    {
        if (_embedded)
        {
            _primary.gameObject.SetActive(false);
            _reset.gameObject.SetActive(false);
            _balance.gameObject.SetActive(false);
            Place(_status.rectTransform, 1568, 46, 0, -36, new Vector2(.5f, 1));
            return;
        }
        if (CreationPage)
        {
            _primary.gameObject.SetActive(false);
            _reset.gameObject.SetActive(false);
            RefreshCreation();
            return;
        }
        var personal = Page == ScreenPage.Personal;
        _reset.gameObject.SetActive(personal);
        _reset.interactable = !_busy && _selected.Count > 0 && (_state.AllowEdits || !Created);
        _primary.gameObject.SetActive(Page != ScreenPage.Characters);
        var label = _primary.GetComponentInChildren<Text>();
        label.text =
            Page == ScreenPage.Summary ? "EDIT SEASONAL PERKS"
            : Page == ScreenPage.Global ? "PERSONAL PERKS"
            : Created ? "REVIEW CHANGES"
            : "REVIEW & CREATE";
        _primary.interactable =
            !_busy
            && (Page != ScreenPage.Summary || _state.CanOpenEditor)
            && (!personal || ((_state.AllowEdits || !Created) && (!Created || Dirty) && ValidSelection()));
        label.color = _primary.interactable ? UiElements.Ink : UiElements.Muted;
        _reset.GetComponentInChildren<Text>().color = _reset.interactable ? UiElements.Ink : UiElements.Muted;
        _balance.color = Remaining < 0 && _state.EnforceBudget && personal ? UiElements.Negative : UiElements.Ink;
        _balance.text =
            personal
                ? $"{_selected.Count} SELECTED     |     {Remaining} POINTS REMAINING"
                    + (_state.EnforceBudget ? "" : "     |     FREE SELECTION")
            : Page == ScreenPage.Global ? $"{_state.Perks.Count(perk => perk.Common && perk.Enabled)} ACTIVE GLOBAL RULES"
            : Page == ScreenPage.Summary
                ? (
                    _state.IsScav ? "SCAV CHARACTER"
                    : _state.ActiveMode == "seasonal" ? $"{_state.Selected.Length} PERSONAL PERKS"
                    : "NORMAL CHARACTER"
                )
            : "ACTIVE CHARACTER: " + _state.ActiveMode.ToUpperInvariant();
        if (personal && !_busy)
        {
            SetMessage(
                Created && !_state.AllowEdits ? "Perk editing is disabled in the server settings."
                : Remaining < 0 && _state.EnforceBudget
                    ? $"Add {Math.Abs(Remaining)} points of detrimental modifiers, or remove beneficial perks."
                : Dirty ? "You have unsaved changes."
                : Created ? "Your saved selection is up to date."
                : "Choose a faction and nickname above, then select your perks."
            );
        }
    }

    private bool ValidSelection()
    {
        return (!_state.EnforceBudget || Remaining >= 0)
            && _state
                .Perks.Where(perk => _selected.Contains(perk.Id))
                .All(perk => string.IsNullOrEmpty(perk.Unavailable) && !perk.Conflicts.Any(_selected.Contains));
    }

    private string LockReason(PerkEntry perk)
    {
        if (!string.IsNullOrEmpty(perk.Unavailable))
        {
            return perk.Unavailable;
        }
        var conflicts = _state
            .Perks.Where(other => _selected.Contains(other.Id) && perk.Conflicts.Contains(other.Id))
            .Select(other => other.Name)
            .ToArray();
        return conflicts.Length == 0 ? "" : "Conflicts with " + string.Join(", ", conflicts) + ".";
    }

    private void Toggle(PerkEntry perk)
    {
        if (_busy || _dialog != null || !PersonalPage || (Created && !_state.AllowEdits))
        {
            return;
        }
        if (!_selected.Remove(perk.Id))
        {
            var reason = LockReason(perk);
            if (reason.Length > 0)
            {
                SetMessage(reason, true);
                return;
            }
            _selected.Add(perk.Id);
        }
        SoundRequested?.Invoke(_selected.Contains(perk.Id) ? InterfaceSound.PerkOn : InterfaceSound.PerkOff);
        RefreshCards();
    }

    private void ResetSelection()
    {
        if (_busy || _dialog != null)
        {
            return;
        }
        Confirm(
            "RESET SELECTION",
            "Clear all personal perks from this draft? Your saved selection remains unchanged until you save.",
            "RESET",
            () =>
            {
                _selected.Clear();
                RefreshCards();
            }
        );
    }

    private void PrimaryAction()
    {
        if (_busy || _dialog != null)
        {
            return;
        }
        if (Page == ScreenPage.Summary && _embedded)
        {
            if (_state.CanOpenEditor)
            {
                EditRequested?.Invoke();
            }
        }
        else if (Page != ScreenPage.Personal)
        {
            ShowPage(ScreenPage.Personal);
        }
        else if (ValidSelection())
        {
            ReviewSelection();
        }
    }

    public void RequestCloseFromInput()
    {
        if (!_busy)
        {
            SoundRequested?.Invoke(InterfaceSound.Back);
            RequestClose();
        }
    }

    public void RequestClose()
    {
        if (_busy)
        {
            return;
        }
        if (_dialog != null)
        {
            DismissDialog();
            return;
        }
        if (CreationPage)
        {
            CreationBack();
            return;
        }
        if (StartupSelection)
        {
            ShowPage(ScreenPage.Characters);
            return;
        }
        WithDiscardConfirmation(() => CloseRequested?.Invoke());
    }

    private void WithDiscardConfirmation(Action continuation)
    {
        if (_dialog != null)
        {
            return;
        }
        if (!Dirty)
        {
            continuation();
            return;
        }
        Confirm(
            "UNSAVED CHANGES",
            "Discard your draft selection before leaving this screen?",
            "DISCARD",
            () =>
            {
                _selected.Clear();
                foreach (var id in _state.Selected)
                {
                    _selected.Add(id);
                }
                continuation();
            }
        );
    }

    public void Dispose()
    {
        _disposed = true;
        DismissDialog();
        _identity?.Dispose();
        _identity = null;
        if (Root)
        {
            Root.SetActive(false);
            UiElements.Destroy(Root);
        }
    }
}
