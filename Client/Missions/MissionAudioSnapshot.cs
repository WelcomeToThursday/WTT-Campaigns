using System.Reflection;
using Comfort.Common;
using Cysharp.Threading.Tasks;
using EFT;
using UnityEngine;

namespace WTT.Campaigns.Client.Missions;

/// <summary>Rewinds the native tinnitus envelope without stopping unrelated audio.</summary>
internal sealed class MissionAudioSnapshot
{
    private static readonly FieldInfo Duration =
        typeof(BetterAudio).GetField("float_1", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException("BetterAudio", "float_1");
    private static readonly FieldInfo Deadline =
        typeof(BetterAudio).GetField("float_2", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException("BetterAudio", "float_2");
    private readonly float _duration,
        _remaining;
    private readonly AudioClip? _clip;

    internal MissionAudioSnapshot(Player player)
    {
        _clip = (AudioClip?)typeof(Player).GetField("_tinnitus", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(player);
        if (!Singleton<BetterAudio>.Instantiated)
            return;
        var audio = Singleton<BetterAudio>.Instance;
        _duration = (float)Duration.GetValue(audio);
        _remaining = Mathf.Max(0, (float)Deadline.GetValue(audio) - Time.time);
    }

    internal async Task RestoreAsync(CancellationToken token)
    {
        if (!Singleton<BetterAudio>.Instantiated)
            return;
        var audio = Singleton<BetterAudio>.Instance;
        Deadline.SetValue(audio, Time.time - 1f);
        // Let the native coroutine restore mixer levels and release its pooled source.
        await UniTask.NextFrame(cancellationToken: token);
        await UniTask.NextFrame(cancellationToken: token);
        if (_remaining <= 0 || _duration <= 0)
            return;
        audio.StartTinnitusEffect(_remaining / 2f, _clip);
        Duration.SetValue(audio, _duration);
        Deadline.SetValue(audio, Time.time + _remaining);
    }
}
