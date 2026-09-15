using EFT;
using UnityEngine;

namespace WTT.Campaigns.Client.Encounters;

// Keep the native corner cursor alive between authoring updates. Identity also
// distinguishes our path from a combat/recovery path to the same destination.
internal sealed class EncounterMovementPath
{
    private AbstractBotPath? _path;
    private Vector3 _target;
    private Vector3 _progressPosition;
    private float _progressTime;

    internal bool Owns(BotMover mover) => _path != null && ReferenceEquals(_path, mover.ActualPathController.CurPath);

    internal bool Keep(BotOwner bot, Vector3 target, EncounterNavigation navigation)
    {
        if (!Owns(bot.Mover) || (_target - target).sqrMagnitude > .01f)
            return false;
        var position = bot.GetPlayer.Transform.position;
        if ((position - _progressPosition).sqrMagnitude >= .0225f)
        {
            _progressPosition = position;
            _progressTime = Time.time;
        }
        return EncounterMovementPolicy.KeepPath(Time.time - _progressTime) && navigation.RemainingPathClear(position, _path!);
    }

    internal void Submitted(BotOwner bot, Vector3 target)
    {
        _path = bot.Mover.ActualPathController.CurPath;
        _target = target;
        _progressPosition = bot.GetPlayer.Transform.position;
        _progressTime = Time.time;
    }
}
