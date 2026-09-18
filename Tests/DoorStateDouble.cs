namespace EFT.Interactive;

// Managed stand-in for exercising the actual state adapter without loading Unity.
public enum EDoorState
{
    Shut,
    Open,
    Locked,
}

public class WorldInteractiveObject
{
    public readonly record struct InteractiveObjectStatusInfo(string Id, EDoorState State, float Angle);
}

public sealed class Door
{
    public string Id = "native-door",
        KeyId = "original-key";
    public EDoorState DoorState = EDoorState.Open;
    public float CurrentAngle = 90;
    public bool CanBeBreached = true,
        CanInteractWithBreach = false,
        Operatable = true,
        Alive = true;
    public int SyncCount;

    public static implicit operator bool(Door door) => door != null && door.Alive;

    public float GetAngle(EDoorState state) => state == EDoorState.Open ? 90 : 0;

    public void SetInitialSyncState(WorldInteractiveObject.InteractiveObjectStatusInfo info)
    {
        DoorState = info.State;
        CurrentAngle = info.Angle;
        SyncCount++;
    }

    public void CheckOcclusionPortal() { }
}
