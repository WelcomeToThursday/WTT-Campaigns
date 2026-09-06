using System;
using System.Collections.Generic;
using System.Linq;
using SeasonalPerks.UI.Controls;
using SeasonalPerks.UI.Models;
using UnityEngine;
using UnityEngine.UI;

namespace SeasonalPerks.UI.Screens;

public sealed partial class SeasonsHubScreen
{
    private GameObject? _dialog;
    private readonly Dictionary<string, int> _exchangeSources = new Dictionary<string, int>();
    private int _exchangeTarget;
    private bool _exchangeCrate;
    public Action<HubAction>? TransactionRequested;
    public bool HasDialog
    {
        get { return _dialog != null; }
    }

    public void DismissDialog()
    {
        if (_dialog)
        {
            UiElements.Destroy(_dialog!);
        }
        _dialog = null;
    }

    private RectTransform Dialog(string title)
    {
        DismissDialog();
        HideTooltip();
        var overlay = Box(_stage, "HubTransactionDialog", 0, 0, 1920, 1080);
        _dialog = overlay.gameObject;
        UiElements.Fill(overlay, new Color(0, 0, 0, .86f), true);
        var panel = Box(overlay, "DialogPanel", 480, 250, 960, 620);
        UiElements.Fill(panel, new Color(.035f, .06f, .052f, 1), true);
        Caption(panel, "Title", title, 28, 32, 22, 850, 48);
        Button(panel, "CANCEL", 32, 544, 180, 42, DismissDialog);
        return panel;
    }

    private void ConfirmClaim(HubReward reward)
    {
        if (!reward.CanClaim || _state.PreviewOnly)
        {
            return;
        }
        var panel = Dialog("CLAIM REWARD");
        Remote(panel, "ClaimImage", reward.Image, 44, 100, 260, 300);
        var costs = string.Join("\n", reward.Costs.Select(c => c.Count + " × " + _state.Documents.First(d => d.Id == c.DocumentId).Name));
        var shortage =
            reward.UniversalNeeded > 0 ? "\n\nUse " + reward.UniversalNeeded + " Classified documents to cover the shortage?" : "";
        Caption(panel, "Confirmation", reward.Name + "\n\n" + costs + shortage, 21, 340, 100, 570, 420);
        Button(
            panel,
            "CONFIRM",
            720,
            544,
            208,
            42,
            () =>
            {
                DismissDialog();
                TransactionRequested?.Invoke(
                    new HubAction
                    {
                        RewardId = reward.Id,
                        ExpectedRevision = _state.Revision,
                        UseClassified = reward.UniversalNeeded > 0,
                    }
                );
            }
        );
    }

    private void OpenExchange()
    {
        _exchangeSources.Clear();
        _exchangeTarget = 0;
        _exchangeCrate = false;
        RenderExchange();
    }

    private void RenderExchange()
    {
        var panel = Dialog("EXCHANGE DOCUMENTS");
        Button(
            panel,
            "DOCUMENT",
            32,
            86,
            260,
            42,
            () =>
            {
                _exchangeCrate = false;
                RenderExchange();
            }
        );
        Button(
            panel,
            "GEAR CRATE",
            310,
            86,
            260,
            42,
            () =>
            {
                _exchangeCrate = true;
                RenderExchange();
            }
        );
        var required = _exchangeCrate ? _state.CrateCost : _state.ExchangeRate;
        var selected = _exchangeSources.Values.Sum();
        Caption(
            panel,
            "ExchangeHelp",
            "Select ordinary documents: " + selected + "/" + required + "\nClassified documents cannot be exchanged.",
            19,
            32,
            140,
            870,
            64
        );
        for (var i = 0; i < _state.Documents.Length; i++)
        {
            var doc = _state.Documents[i];
            var x = 32 + i * 112;
            var count = _exchangeSources.TryGetValue(doc.Id, out var value) ? value : 0;
            var icon = Remote(panel, "ExchangeSource" + i, doc.Image, x, 224, 100, 100);
            Hint(icon.gameObject, doc.Name, 500 + x, 390);
            Caption(panel, "ExchangeCount" + i, count + "/" + doc.Count, 18, x, 330, 100, 28);
            Button(
                panel,
                "+",
                x,
                367,
                46,
                32,
                () =>
                {
                    if (count < doc.Count && selected < required)
                    {
                        _exchangeSources[doc.Id] = count + 1;
                        RenderExchange();
                    }
                }
            );
            Button(
                panel,
                "−",
                x + 52,
                367,
                46,
                32,
                () =>
                {
                    if (count > 0)
                    {
                        _exchangeSources[doc.Id] = count - 1;
                        RenderExchange();
                    }
                }
            );
        }
        var reason = _exchangeCrate ? _state.CrateUnavailableReason : _state.ExchangeUnavailableReason;
        if (!_exchangeCrate && _state.Documents.Length > 0)
        {
            var target = _state.Documents[_exchangeTarget];
            Button(
                panel,
                "‹",
                32,
                438,
                44,
                42,
                () =>
                {
                    _exchangeTarget = (_exchangeTarget + _state.Documents.Length - 1) % _state.Documents.Length;
                    RenderExchange();
                }
            );
            Caption(panel, "ExchangeTarget", "Receive: " + target.Name, 20, 90, 438, 700, 42);
            Button(
                panel,
                "›",
                860,
                438,
                44,
                42,
                () =>
                {
                    _exchangeTarget = (_exchangeTarget + 1) % _state.Documents.Length;
                    RenderExchange();
                }
            );
        }
        else
        {
            Caption(panel, "ExchangeTarget", reason.Length > 0 ? reason : "Receive: Black Division gear crate", 18, 32, 425, 860, 90);
        }
        var confirm = Button(
            panel,
            "EXCHANGE",
            720,
            544,
            208,
            42,
            () =>
            {
                DismissDialog();
                TransactionRequested?.Invoke(
                    new HubAction
                    {
                        Action = "exchange",
                        ExpectedRevision = _state.Revision,
                        Crate = _exchangeCrate,
                        DocumentId = _state.Documents[_exchangeTarget].Id,
                        Sources = _exchangeSources.Where(p => p.Value > 0).ToDictionary(p => p.Key, p => p.Value),
                    }
                );
            }
        );
        confirm.interactable = selected == required && reason.Length == 0;
        if (reason.Length > 0)
        {
            Hint(confirm.gameObject, reason, 1120, 680);
        }
    }

    public void ShowResult(string message)
    {
        var panel = Dialog("SEASONAL HUB");
        Caption(panel, "Result", message, 23, 60, 140, 840, 260).alignment = TextAnchor.MiddleCenter;
        Button(panel, "OK", 720, 544, 208, 42, DismissDialog);
    }

    private void ClaimAction(Transform parent, HubReward reward, float x, float y, float width)
    {
        var button = Button(parent, reward.Claimed ? "CLAIMED" : "CLAIM REWARD", x, y, width, 36, () => ConfirmClaim(reward));
        button.interactable = reward.CanClaim && !_state.PreviewOnly;
        if (!button.interactable)
        {
            Hint(
                button.gameObject,
                reward.UnavailableReason.Length > 0 ? reward.UnavailableReason : "Not available in this preview.",
                x,
                y - 95
            );
        }
    }
}
