using EFT;
using UnityEngine;

namespace WTT.Campaigns.Client.Encounters;

// Keep the native corner cursor alive between authoring updates. Identity also
// distinguishes our path from a combat/recovery path to the same destination.
internal sealed class EncounterMovementPath
{
    private AbstractBotPath? _path;
    private Vector3 _target;
    private readonly EncounterPathProgress _progress = new();
    private bool _canRetain;
    internal string RepathReason { get; private set; } = "Initial path";

    internal bool Owns(BotMover mover) => _path != null && ReferenceEquals(_path, mover.ActualPathController.CurPath);

    internal int LastCorner => (_path?.Length ?? 0) - 1;
    internal int CurrentCorner => _path?.CurIndex ?? -1;

    internal bool PassedCorner(BotMover mover, int index) => Owns(mover) && index >= 0 && _path!.CurIndex > index;

    internal Vector3[] RemainingCorners(BotOwner bot)
    {
        if (!Owns(bot.Mover) || _path!.CurIndex < 0 || _path.CurIndex >= _path.Length)
            return Array.Empty<Vector3>();
        var result = new Vector3[_path.Length - _path.CurIndex + 1];
        result[0] = bot.GetPlayer.Transform.position;
        for (var i = _path.CurIndex; i < _path.Length; i++)
            result[i - _path.CurIndex + 1] = _path.GetPoint(i);
        return result;
    }

    internal Vector3 Direction(BotOwner bot, Vector3 target)
    {
        var position = bot.GetPlayer.Transform.position;
        if (Owns(bot.Mover) && (_target - target).sqrMagnitude <= .01f)
            for (var i = Math.Max(0, _path!.CurIndex); i < _path.Length; i++)
            {
                var direction = _path.GetPoint(i) - position;
                if (direction.sqrMagnitude > .01f)
                    return direction.normalized;
            }
        return (target - position).normalized;
    }

    internal bool Keep(BotOwner bot, Vector3 target, EncounterNavigation navigation)
    {
        _canRetain = false;
        if (!Owns(bot.Mover) || (_target - target).sqrMagnitude > .01f)
        {
            RepathReason = "Target or native ownership changed";
            return false;
        }
        var position = bot.GetPlayer.Transform.position;
        if (!_progress.Observe(Remaining(position), Time.time))
        {
            RepathReason = "No forward path progress for 3 seconds";
            navigation.InvalidateCurves();
            return false;
        }
        if (navigation.RemainingPathClear(position, _path!))
            return true;
        RepathReason = "Remaining path needs validation";
        navigation.InvalidateCurves();
        _canRetain = true;
        return false;
    }

    // NavMesh raycasts along an edge can disagree with CalculatePath. If a full
    // clearance-validated recalculation returns the same remaining corners, keep
    // the native cursor instead of sending it back to a new origin every refresh.
    internal bool TryRetain(BotOwner bot, Vector3 target, Vector3[] validatedCorners)
    {
        if (!_canRetain || !Owns(bot.Mover) || (_target - target).sqrMagnitude > .01f)
            return false;
        var path = _path!;
        return EncounterMovementPolicy.SameRemainingCorners(
            path.Length,
            path.CurIndex,
            validatedCorners.Length,
            (oldIndex, freshIndex) => (path.GetPoint(oldIndex) - validatedCorners[freshIndex]).sqrMagnitude
        );
    }

    private float Remaining(Vector3 position)
    {
        if (_path == null || _path.CurIndex < 0 || _path.CurIndex >= _path.Length)
            return float.PositiveInfinity;
        var distance = 0f;
        for (var i = _path.CurIndex; i < _path.Length; i++)
        {
            var corner = _path.GetPoint(i);
            distance += Vector3.Distance(position, corner);
            position = corner;
        }
        return distance;
    }

    internal void Submitted(BotOwner bot, Vector3 target)
    {
        _path = bot.Mover.ActualPathController.CurPath;
        _target = target;
        _canRetain = false;
        _progress.Reset(Remaining(bot.GetPlayer.Transform.position), Time.time);
    }
}
