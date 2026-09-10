using Microsoft.AspNetCore.Components;
using WTT.Campaigns.Server.Web.Authoring;

namespace WTT.Campaigns.Server.Web.Pages;

public partial class Creator
{
    [SupplyParameterFromQuery(Name = "help")]
    public bool? HelpQuery { get; set; }

    [SupplyParameterFromQuery(Name = "tutorial")]
    public string? TutorialQuery { get; set; }

    [SupplyParameterFromQuery(Name = "step")]
    public int? TutorialStepQuery { get; set; }

    private bool _showHelp,
        _tutorialPaused,
        _guideQueryApplied;
    private string _tutorialTrack = "",
        _tutorialFinished = "";
    private int _tutorialStep;

    private void ApplyGuidanceQuery()
    {
        if (_guideQueryApplied)
        {
            return;
        }

        _guideQueryApplied = true;
        _showHelp = HelpQuery == true;
        if (TutorialQuery is "basics" or "story")
        {
            StartTutorial(TutorialQuery);
            _tutorialStep = Math.Clamp(
                TutorialStepQuery ?? 0,
                0,
                (TutorialQuery == "story" ? CreatorGuidance.Story : CreatorGuidance.Basics).Length - 1
            );
        }
    }

    private void StartTutorial(string track)
    {
        _tutorialTrack = track == "story" ? "story" : "basics";
        _tutorialStep = 0;
        _tutorialPaused = false;
        _tutorialFinished = "";
        _showHelp = false;
    }

    private void OpenGuideSection(string section)
    {
        if (_draft == null || !Sections.Contains(section))
        {
            return;
        }

        if (_section != section)
        {
            _section = section;
            _focusId = "";
            _focusChildId = "";
        }

        _showHelp = false;
    }

    private void FinishTutorial()
    {
        _tutorialFinished = _tutorialTrack == "story" ? "Your first story quest" : "Campaign basics";
        _tutorialTrack = "";
    }
}
