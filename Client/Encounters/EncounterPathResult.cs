using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Client.Encounters;

internal enum EncounterPathStatus
{
    Pending,
    Complete,
    Failed,
}

// Local diagnostics only; never persisted with an authored route.
internal sealed class EncounterPathResult
{
    internal EncounterPathStatus Status { get; }
    internal SpatialVector[] Corners { get; }
    internal string Reason { get; }
    internal float Distance { get; }

    internal EncounterPathResult(EncounterPathStatus status, SpatialVector[]? corners = null, string reason = "")
    {
        Status = status;
        Corners = corners ?? Array.Empty<SpatialVector>();
        Reason = reason;
        for (var i = 1; i < Corners.Length; i++)
        {
            var a = Corners[i - 1];
            var b = Corners[i];
            Distance += (float)
                Math.Sqrt((double)(a.X - b.X) * (a.X - b.X) + (double)(a.Y - b.Y) * (a.Y - b.Y) + (double)(a.Z - b.Z) * (a.Z - b.Z));
        }
    }
}
