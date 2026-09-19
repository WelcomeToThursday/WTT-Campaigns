namespace WTT.Campaigns.Shared.Spatial;

public sealed class PatrolCheckpoint
{
    public string RouteId { get; set; } = "";
    public int Waypoint { get; set; } = -1;
    public int Direction { get; set; } = 1;
    public double? WaitRemaining { get; set; }
    public bool Completed { get; set; }
}
