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
    internal SplinePathFailure Failure { get; }
    internal int FailedSample { get; }
    private readonly float[] _distances;
    internal bool HasProblemLocation => Status == EncounterPathStatus.Failed && FailedSample >= 0 && FailedSample < Corners.Length;
    internal uint DiagnosticColor =>
        Status switch
        {
            EncounterPathStatus.Complete => 0x59D973,
            EncounterPathStatus.Pending => 0xFFBF40,
            _ => Failure switch
            {
                SplinePathFailure.OffNavMesh => 0xEC63FF,
                SplinePathFailure.StandingClearance => 0xFF9838,
                _ => 0xFF594D,
            },
        };

    internal EncounterPathResult(
        EncounterPathStatus status,
        SpatialVector[]? corners = null,
        string reason = "",
        SplinePathFailure failure = SplinePathFailure.None,
        int failedSample = -1
    )
    {
        Status = status;
        Corners = corners ?? Array.Empty<SpatialVector>();
        Reason = reason;
        Failure = failure;
        FailedSample = failedSample;
        _distances = new float[Corners.Length];
        for (var i = 1; i < Corners.Length; i++)
        {
            var a = Corners[i - 1];
            var b = Corners[i];
            Distance += (float)
                Math.Sqrt((double)(a.X - b.X) * (a.X - b.X) + (double)(a.Y - b.Y) * (a.Y - b.Y) + (double)(a.Z - b.Z) * (a.Z - b.Z));
            _distances[i] = Distance;
        }
    }

    // Fade by distance along the route, not world proximity: nearby hairpins
    // must not acquire another leg's warning. Grey portions remain unconfirmed.
    internal uint SegmentColor(int end)
    {
        if (!HasProblemLocation || end < 1 || end >= Corners.Length)
            return DiagnosticColor;
        var start = Failure == SplinePathFailure.BlockedSegment ? Math.Max(0, FailedSample - 1) : FailedSample;
        var distance = Math.Max(0, Math.Max(_distances[start] - _distances[end], _distances[end - 1] - _distances[FailedSample]));
        var weight = Math.Clamp(1 - distance / 3f, 0, 1);
        weight *= weight * (3 - 2 * weight);
        const uint neutral = 0xBFCBD4;
        uint Channel(int shift) =>
            (uint)Math.Round(((neutral >> shift) & 255) * (1 - weight) + ((DiagnosticColor >> shift) & 255) * weight);
        return (Channel(16) << 16) | (Channel(8) << 8) | Channel(0);
    }
}
