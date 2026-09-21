using System.Numerics;

namespace WTT.Campaigns.Shared.Spatial;

/// <summary>Retains progress across native ownership changes. A rejoin never moves the curve cursor backwards.</summary>
public sealed class SplineFollower
{
    private int _from = -1,
        _to = -1;
    public int NextSample { get; private set; }

    public void Select(int from, int to, IReadOnlyList<Vector3> points, Vector3 position)
    {
        if (_from == from && _to == to)
        {
            NextSample = Math.Min(NextSample, points.Count - 1);
            return;
        }
        _from = from;
        _to = to;
        NextSample = 0;
        var nearest = float.PositiveInfinity;
        // Initial joining may enter anywhere on this leg. Future resumptions retain the native cursor.
        for (var i = 0; i < points.Count; i++)
        {
            var distance = Vector3.DistanceSquared(position, points[i]);
            if (distance < nearest)
            {
                nearest = distance;
                NextSample = i;
            }
        }
    }

    public void Advance(int sample) => NextSample = Math.Max(NextSample, sample);

    public void BeginLeg(int from, int to)
    {
        _from = from;
        _to = to;
        NextSample = 0;
    }

    public void Reset()
    {
        _from = _to = -1;
        NextSample = 0;
    }
}
