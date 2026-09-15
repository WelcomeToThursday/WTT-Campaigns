using WTT.Campaigns.Client.Authoring;

namespace WTT.Campaigns.Tests;

internal static class EditorTooltipChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var shortHint = EditorTooltipPlacement.Place(440, 474, 70, 100, 78, 34, 525, 720);
        check(shortHint == (418f, 106f), "Short toolbar hint stays centered below its control near the right edge");
        var wideHint = EditorTooltipPlacement.Place(440, 474, 70, 100, 360, 80, 525, 720);
        check(wideHint == (157f, 106f), "Long tooltip clamps using its actual measured width");
        var bottomHint = EditorTooltipPlacement.Place(200, 234, 670, 700, 120, 60, 525, 720);
        check(bottomHint == (157f, 604f), "Bottom toolbar hint flips above its control");
        var leftHint = EditorTooltipPlacement.Place(0, 34, 70, 100, 120, 34, 525, 720);
        check(leftHint.X == 8, "Left edge tooltip retains the screen margin");
        var tiny = EditorTooltipPlacement.Place(0, 34, 0, 30, 360, 80, 200, 60);
        check(tiny == (8f, 8f), "A constrained viewport does not produce inverted clamp bounds");
    }
}
