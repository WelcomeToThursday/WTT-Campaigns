using UnityEngine;

namespace SeasonalPerks.Client.Hub;

public sealed partial class SeasonHubUi
{
    // Keep the local walkthrough independent of EFT's own tutorial preferences.
    private const string TutorialPreference = "WTTSeasonal/UITutorial/BattlePass/v1";
    private bool _tutorialOffered;
    private int _tutorialInputFrame = -1;

    private void OfferTutorial()
    {
        if (
            !IsOpen
            || _screen!.HasDialog
            || _tutorialOffered
            || _pendingBody != null
            || _transacting
            || PlayerPrefs.GetInt(TutorialPreference, 0) != 0
        )
        {
            return;
        }
        _tutorialOffered = true;
        _screen!.StartTutorial();
        _tutorialInputFrame = Time.frameCount;
    }

    private static void CompleteTutorial()
    {
        PlayerPrefs.SetInt(TutorialPreference, 1);
        PlayerPrefs.Save();
    }

    private void UpdateTutorialInput()
    {
        if (Time.frameCount == _tutorialInputFrame)
        {
            return;
        }
        _tutorialInputFrame = Time.frameCount;
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            _screen!.DismissTutorial();
        }
        else if (Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.LeftArrow))
        {
            _screen!.PreviousTutorialStep();
        }
        else if (
            Input.GetKeyDown(KeyCode.E)
            || Input.GetKeyDown(KeyCode.RightArrow)
            || Input.GetKeyDown(KeyCode.Space)
            || Input.GetKeyDown(KeyCode.Return)
            || Input.GetKeyDown(KeyCode.KeypadEnter)
            || (
                _screen!.TutorialStep == 7
                && Input.anyKeyDown
                && !Input.GetMouseButtonDown(0)
                && !Input.GetMouseButtonDown(1)
                && !Input.GetMouseButtonDown(2)
            )
        )
        {
            _screen!.AdvanceTutorial();
        }
    }
}
