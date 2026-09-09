namespace SeasonalPerks.Shared.Story;

public static class StoryPlaybackRules
{
    public static bool WaitForContinue(StoryDialogLine line, bool moreLines, bool closed) =>
        line.Side == "Npc" && line.Playback.Sound.Length == 0 && line.Playback.LipSyncs.Count == 0 && (moreLines || closed);
}
