using Cysharp.Text;
using WTT.Campaigns.Shared.Story;

namespace WTT.Campaigns.Client.Story;

internal static class StorySubtitleText
{
    // Keep pooled buffers within a synchronous call, never across the playback await.
    internal static string Build(StorySequence[] subtitles, float time, Func<string, string> localize, string previous)
    {
        using var text = ZString.CreateStringBuilder();
        var first = true;
        foreach (var subtitle in subtitles)
        {
            if (!(time >= subtitle.Start && time < subtitle.End))
                continue;
            if (!first)
                text.Append('\n');
            first = false;
            text.Append(localize(subtitle.Key));
        }
        // Unity's legacy Text needs a string; reuse it while the visible content is unchanged.
        return text.AsSpan().SequenceEqual(previous.AsSpan()) ? previous : text.ToString();
    }
}
