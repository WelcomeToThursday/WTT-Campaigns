using System;
using System.Linq;
using SeasonalPerks.UI.Controls;
using SeasonalPerks.UI.Models;
using UnityEngine;
using UnityEngine.UI;

namespace SeasonalPerks.UI.Screens;

public sealed partial class SeasonsHubScreen
{
    private GameObject? _dialog;
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
        DismissTutorial();
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
        if (HasTutorial || !reward.CanClaim || _state.PreviewOnly)
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
