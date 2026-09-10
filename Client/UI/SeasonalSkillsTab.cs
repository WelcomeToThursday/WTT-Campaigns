using EFT.UI;
using HarmonyLib;
using SeasonalPerks.UI.Controls;
using SeasonalPerks.UI.Screens;
using TMPro;
using UnityEngine;
using ZLinq;

namespace SeasonalPerks.Client.UI;

public sealed class SeasonalSkillsTab : MonoBehaviour, ITabController
{
    private Tab? _tab;
    private TabGroup? _group;
    private GameObject? _host;
    private SeasonalScreen? _view;
    private int _generation;
    private bool _isScav;

    internal void Initialize(SkillsAndMasteringScreen screen, bool isScav)
    {
        _isScav = isScav;
        if (_tab && _host && _group != null)
        {
            return;
        }
        var group = (TabGroup?)AccessTools.Field(typeof(SkillsAndMasteringScreen), "_skillMasterTabGroup").GetValue(screen);
        if (group == null)
        {
            throw new InvalidOperationException("EFT has not initialized the skills tab group.");
        }
        var tabsField = AccessTools.Field(typeof(TabGroup), "_tabs");
        Release();
        var tabs = (Tab[])tabsField.GetValue(group);
        _group = group;
        try
        {
            _tab = Instantiate(screen._masteringTab, screen._masteringTab.transform.parent, false);
            _tab.name = "SeasonalPerksTab";
            _tab.transform.SetSiblingIndex(screen._masteringTab.transform.GetSiblingIndex() + 1);
            var rect = (RectTransform)_tab.transform;
            if (!_tab.transform.parent.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>())
            {
                rect.anchoredPosition += new Vector2(((RectTransform)screen._masteringTab.transform).rect.width + 12, 0);
            }
            foreach (var localized in _tab.GetComponentsInChildren<LocalizedText>(true))
            {
                localized.enabled = false;
            }
            foreach (var label in _tab.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                label.text = "MODIFIERS";
            }
            foreach (var version in new[] { _tab._normalVersion, _tab._selectedVersion })
            {
                if (!version)
                {
                    continue;
                }
                foreach (
                    var icon in version
                        .GetComponentsInChildren<UnityEngine.UI.Image>(true)
                        .AsValueEnumerable()
                        .Where(image => image.name == "Icon")
                )
                {
                    SeasonUi.Instance.LoadArtwork(
                        version == _tab._selectedVersion ? "modifiers-tab-selected" : "modifiers-tab-normal",
                        icon
                    );
                }
            }
            _tab.Init(this);
            _tab.UpdateVisual(false);
            var source = (RectTransform)screen._skillsScreen.transform;
            var host = UiElements.Rect("SeasonalPerksContent", source.parent, source.sizeDelta.x, source.sizeDelta.y);
            host.anchorMin = source.anchorMin;
            host.anchorMax = source.anchorMax;
            host.pivot = source.pivot;
            host.anchoredPosition = source.anchoredPosition;
            host.localScale = source.localScale;
            _host = host.gameObject;
            _host.SetActive(false);
            // Register only once the controller and content are ready for selection.
            tabsField.SetValue(_group, tabs.AsValueEnumerable().Concat(new[] { _tab }).ToArray());
            _tab.OnSelectionChanged += _group.SelectionChangedHandler;
        }
        catch
        {
            Release();
            throw;
        }
    }

    public async void Show()
    {
        var generation = ++_generation;
        try
        {
            if (!_host)
            {
                throw new InvalidOperationException("The seasonal perks tab is not initialized.");
            }
            _host!.SetActive(true);
            _view ??= SeasonUi.Instance.CreateView(_host.transform, true);
            _view.EditRequested = () =>
            {
                if (Plugin.InRaid)
                {
                    _view.SetMessage("Finish the raid before editing seasonal perks.");
                }
                else
                {
                    SeasonUi.Instance.Open(ScreenPage.Personal);
                }
            };
            _view.SetBusy(false);
            _view.Open(ScreenPage.Summary);
            _view.SetBusy(true, "Loading active perks...");
            var snapshot = await Plugin.Request("snapshot");
            if (generation != _generation || !_host || !_host.activeInHierarchy)
            {
                return;
            }
            _view.SetBusy(false);
            var state = SeasonUi.Presentation(snapshot);
            state.CanOpenEditor =
                !Plugin.InRaid
                && (
                    state.AllowEdits
                    || !state.Characters.AsValueEnumerable().Any(character => character.Mode == "seasonal" && character.Exists)
                );
            state.IsScav = _isScav;
            if (_isScav)
            {
                state.ActiveMode = "normal";
                state.Selected = Array.Empty<string>();
            }
            _view.SetState(state, ScreenPage.Summary);
        }
        catch (Exception exception)
        {
            Plugin.Error(exception);
            if (generation == _generation)
            {
                _view?.SetBusy(false);
                _view?.SetMessage(exception.Message, true);
            }
        }
    }

    public Task<bool> TryHide()
    {
        _generation++;
        if (_host)
        {
            _host!.SetActive(false);
        }
        return Task.FromResult(true);
    }

    private void Update()
    {
        if (_host && _host!.activeInHierarchy)
        {
            _view?.Fit();
        }
    }

    private void OnDestroy()
    {
        Release();
    }

    private void Release()
    {
        _generation++;
        if (_tab && _group != null)
        {
            _tab!.OnSelectionChanged -= _group.SelectionChangedHandler;
            var tabsField = AccessTools.Field(typeof(TabGroup), "_tabs");
            var tabs = (Tab[])tabsField.GetValue(_group);
            tabsField.SetValue(_group, tabs.AsValueEnumerable().Where(tab => tab != _tab).ToArray());
        }
        _view?.Dispose();
        _view = null;
        if (_host)
        {
            Destroy(_host);
        }
        _host = null;
        if (_tab)
        {
            _tab!.gameObject.SetActive(false);
            Destroy(_tab.gameObject);
        }
        _tab = null;
        _group = null;
    }
}
