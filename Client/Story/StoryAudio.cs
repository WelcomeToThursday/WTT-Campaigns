using Comfort.Common;
using EFT.UI;
using UnityEngine;

namespace SeasonalPerks.Client.Story;

internal static class StoryAudio
{
    internal static void Configure(AudioSource source)
    {
        source.spatialBlend = 0;
        if (Singleton<GUISounds>.Instantiated)
        {
            source.outputAudioMixerGroup = Singleton<GUISounds>.Instance.MasterMixer.FindMatchingGroups("UI").FirstOrDefault();
        }
    }
}
