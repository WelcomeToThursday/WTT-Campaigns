using System.Numerics;

namespace WTT.Campaigns.Client.Encounters;

// Local edits of a calculated route, never a replacement for NavMesh routing.
// Every inserted point and connection must be accepted by the live adapter.
internal static class EncounterRouteClearance
{
    internal const float Radius = .4f;
    internal const float PreferredRadius = .55f;
    internal const int CandidateLimit = 24;
    internal readonly struct Obstacle(Vector3 center, Vector3 extents)
    {
        internal Vector3 Center { get; } = center;
        internal Vector3 Extents { get; } = extents;
    }

    internal static Vector3[] Adjust(
        Vector3[] original,
        Func<Vector3, Vector3?> sample,
        Func<Vector3, Vector3, bool> clear,
        Func<Vector3, Vector3, Obstacle?> obstacle)
    {
        if (original.Length < 2)
            return original;
        var points = new List<Vector3>(original);
        var attempts = 0;
        // Give bends room first. This also handles corners of large containers,
        // without trying to route around the bounds of the entire container.
        for (var i = 1; i < points.Count - 1 && attempts < CandidateLimit; i++)
        {
            var before = points[i - 1];
            var current = points[i];
            var after = points[i + 1];
            if (clear(before, current) && clear(current, after))
                continue;
            Vector3? best = null;
            var cost = float.PositiveInfinity;
            for (var direction = 0; direction < 8 && attempts < CandidateLimit; direction++, attempts++)
            {
                var angle = direction * MathF.PI / 4;
                var proposed = current + new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle)) * .7f;
                var candidate = sample(proposed);
                if (!candidate.HasValue || !clear(before, candidate.Value) || !clear(candidate.Value, after))
                    continue;
                var distance = Vector3.Distance(before, candidate.Value) + Vector3.Distance(candidate.Value, after);
                if (distance >= cost)
                    continue;
                best = candidate;
                cost = distance;
            }
            if (best.HasValue)
                points[i] = best.Value;
        }
        // A small prop can sit on an otherwise straight baked segment. Try both
        // sides of its padded footprint, with approach and departure points.
        for (var i = 1; i < points.Count && attempts < CandidateLimit; i++)
        {
            var from = points[i - 1];
            var to = points[i];
            if (clear(from, to))
                continue;
            var bounds = obstacle(from, to);
            var travel = new Vector3(to.X - from.X, 0, to.Z - from.Z);
            if (!bounds.HasValue || travel.LengthSquared() < .01f)
                continue;
            var b = bounds.Value;
            if (b.Extents.X > 2 || b.Extents.Z > 2)
                continue;
            travel = Vector3.Normalize(travel);
            var side = new Vector3(-travel.Z, 0, travel.X);
            var along = MathF.Abs(travel.X) * b.Extents.X + MathF.Abs(travel.Z) * b.Extents.Z + PreferredRadius + .15f;
            var across = MathF.Abs(side.X) * b.Extents.X + MathF.Abs(side.Z) * b.Extents.Z + PreferredRadius + .15f;
            Vector3[]? best = null;
            var cost = float.PositiveInfinity;
            foreach (var sign in new[] { -1, 1 })
            {
                if (attempts++ >= CandidateLimit)
                    break;
                var center = new Vector3(b.Center.X, from.Y, b.Center.Z);
                var first = sample(center - travel * along + side * (across * sign));
                var last = sample(center + travel * along + side * (across * sign));
                if (!first.HasValue || !last.HasValue || !clear(from, first.Value)
                    || !clear(first.Value, last.Value) || !clear(last.Value, to))
                    continue;
                var distance = Vector3.Distance(from, first.Value) + Vector3.Distance(first.Value, last.Value) + Vector3.Distance(last.Value, to);
                if (distance >= cost)
                    continue;
                best = new[] { first.Value, last.Value };
                cost = distance;
            }
            if (best != null)
            {
                points.InsertRange(i, best);
                i += best.Length;
            }
        }
        return points.ToArray();
    }
}

internal static class EncounterObstaclePolicy
{
    internal static bool WalkableContact(float normalY, float heightAboveFeet) => normalY >= .65f && heightAboveFeet <= .15f;
}
