using EFT.Interactive;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Client.Authoring.Scenes;

internal sealed class SceneDoorState
{
    internal readonly Door Door;
    private readonly EDoorState _state;
    private readonly float _angle;
    private readonly string _key;
    private readonly bool _breach,
        _interact,
        _operate;
    private string? _signature;

    internal SceneDoorState(Door door)
    {
        Door = door;
        _state = door.DoorState;
        _angle = door.CurrentAngle;
        _key = door.KeyId;
        _breach = door.CanBeBreached;
        _interact = door.CanInteractWithBreach;
        _operate = door.Operatable;
    }

    internal void Apply(MapDoorEdit edit)
    {
        var signature = Newtonsoft.Json.JsonConvert.SerializeObject(
            new
            {
                edit.State,
                edit.KeyId,
                edit.CanBeBreached,
                edit.Operatable,
            }
        );
        if (_signature == signature)
            return; // Starting state is applied once, not forced after a player unlocks/opens it.
        Door.KeyId = edit.KeyId ?? _key;
        Door.CanBeBreached = edit.CanBeBreached ?? _breach;
        Door.CanInteractWithBreach = edit.CanBeBreached ?? _interact;
        Door.Operatable = edit.Operatable ?? _operate;
        var state = edit.State == "Unchanged" ? _state : (EDoorState)Enum.Parse(typeof(EDoorState), edit.State);
        Door.SetInitialSyncState(new WorldInteractiveObject.InteractiveObjectStatusInfo(Door.Id, state, Door.GetAngle(state)));
        if (edit.State == "Unchanged")
            Door.CurrentAngle = _angle;
        Door.CheckOcclusionPortal();
        _signature = signature;
    }

    internal void Restore()
    {
        if (!Door)
            return;
        Door.KeyId = _key;
        Door.CanBeBreached = _breach;
        Door.CanInteractWithBreach = _interact;
        Door.Operatable = _operate;
        Door.SetInitialSyncState(new WorldInteractiveObject.InteractiveObjectStatusInfo(Door.Id, _state, _angle));
        Door.CurrentAngle = _angle;
        Door.CheckOcclusionPortal();
    }
}
