using System.Numerics;

namespace WTT.Campaigns.Shared.Spatial;

/// <summary>Portable cubic Bézier geometry. Handles are offsets from their knot.</summary>
public sealed class SpatialSpline
{
    public bool Closed { get; set; }
    public float Strength { get; set; } = .5f;
    public List<SplineKnot> Knots { get; set; } = new();
}

public sealed class SplineKnot
{
    public const string Corner = "Corner",
        Auto = "Auto",
        Aligned = "Aligned",
        Free = "Free";
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..24];

    // Optional external identity; interpreted by the owner, never by spline mathematics.
    public string AnchorId { get; set; } = "";
    public SpatialVector Position { get; set; } = new();
    public SpatialVector Incoming { get; set; } = new();
    public SpatialVector Outgoing { get; set; } = new();
    public string Mode { get; set; } = Corner;

    public bool ShouldSerializeAnchorId() => AnchorId.Length > 0;
}

public readonly struct SplineSample(Vector3 position, int segment, float t)
{
    public Vector3 Position { get; } = position;
    public int Segment { get; } = segment;
    public float T { get; } = t;
}

/// <summary>No Unity, navigation, or route semantics. Sampling is bounded and fails explicitly.</summary>
public static class SplineGeometry
{
    public const int MaxKnots = 2048,
        MaxSamples = 32768;

    public static Vector3 Vector(SpatialVector v) => new(v.X, v.Y, v.Z);

    public static SpatialVector Spatial(Vector3 v) =>
        new()
        {
            X = v.X,
            Y = v.Y,
            Z = v.Z,
        };

    public static int SegmentCount(SpatialSpline s) =>
        s.Knots.Count < 2 ? 0
        : s.Closed ? s.Knots.Count
        : s.Knots.Count - 1;

    public static Vector3 Evaluate(SpatialSpline s, int segment, float t)
    {
        Controls(s, segment, out var a, out var b, out var c, out var d);
        var u = 1 - Math.Clamp(t, 0, 1);
        t = 1 - u;
        return u * u * u * a + 3 * u * u * t * b + 3 * u * t * t * c + t * t * t * d;
    }

    public static Vector3 Tangent(SpatialSpline s, int segment, float t)
    {
        Controls(s, segment, out var a, out var b, out var c, out var d);
        t = Math.Clamp(t, 0, 1);
        var v = 3 * (1 - t) * (1 - t) * (b - a) + 6 * (1 - t) * t * (c - b) + 3 * t * t * (d - c);
        return v.LengthSquared() > 1e-12f ? Vector3.Normalize(v) : Vector3.Zero;
    }

    private static void Controls(SpatialSpline s, int segment, out Vector3 a, out Vector3 b, out Vector3 c, out Vector3 d)
    {
        if (segment < 0 || segment >= SegmentCount(s))
            throw new ArgumentOutOfRangeException(nameof(segment));
        var first = s.Knots[segment];
        var last = s.Knots[(segment + 1) % s.Knots.Count];
        a = Vector(first.Position);
        d = Vector(last.Position);
        // Corner handles collapse to the anchor, giving an exact straight segment when both ends are corners.
        b = a + (first.Mode == SplineKnot.Corner ? Vector3.Zero : Vector(first.Outgoing));
        c = d + (last.Mode == SplineKnot.Corner ? Vector3.Zero : Vector(last.Incoming));
    }

    public static List<SplineSample> Sample(SpatialSpline s, float tolerance = .025f, float spacing = .5f)
    {
        if (!float.IsFinite(tolerance) || tolerance <= 0 || !float.IsFinite(spacing) || spacing <= 0)
            throw new ArgumentOutOfRangeException(nameof(tolerance));
        var error = Error(s);
        if (error.Length > 0)
            throw new ArgumentException(error);
        var result = new List<SplineSample>();
        if (s.Knots.Count == 0)
            return result;
        result.Add(new(Vector(s.Knots[0].Position), 0, 0));
        for (var segment = 0; segment < SegmentCount(s); segment++)
        {
            Controls(s, segment, out var a, out var b, out var c, out var d);
            Subdivide(a, b, c, d, 0, 1, 0, segment);
        }
        return result;

        void Subdivide(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float from, float to, int depth, int segment)
        {
            var flat = Math.Max(LineDistance(b, a, d), LineDistance(c, a, d)) <= tolerance;
            if (flat && Vector3.Distance(a, d) <= spacing)
            {
                if (result.Count >= MaxSamples)
                    throw new ArgumentException("Spline is too detailed; shorten it or remove shaping points.");
                result.Add(new(d, segment, to));
                return;
            }
            if (depth >= 20)
                throw new ArgumentException("Spline exceeds the supported sampling precision.");
            var ab = (a + b) / 2;
            var bc = (b + c) / 2;
            var cd = (c + d) / 2;
            var abc = (ab + bc) / 2;
            var bcd = (bc + cd) / 2;
            var middle = (abc + bcd) / 2;
            var t = (from + to) / 2;
            Subdivide(a, ab, abc, middle, from, t, depth + 1, segment);
            Subdivide(middle, bcd, cd, d, t, to, depth + 1, segment);
        }
    }

    private static float LineDistance(Vector3 p, Vector3 a, Vector3 b)
    {
        var delta = b - a;
        var t = delta.LengthSquared() > 1e-12f ? Math.Clamp(Vector3.Dot(p - a, delta) / delta.LengthSquared(), 0, 1) : 0;
        return Vector3.Distance(p, a + delta * t);
    }

    public static float Distance(SpatialSpline s)
    {
        var samples = Sample(s);
        var length = 0f;
        for (var i = 1; i < samples.Count; i++)
            length += Vector3.Distance(samples[i - 1].Position, samples[i].Position);
        return length;
    }

    public static SplineKnot Split(SpatialSpline s, int segment, float t)
    {
        if (s.Knots.Count >= MaxKnots || !float.IsFinite(t) || t <= 0 || t >= 1)
            throw new ArgumentOutOfRangeException(nameof(t));
        Controls(s, segment, out var a, out var b, out var c, out var d);
        var ab = Vector3.Lerp(a, b, t);
        var bc = Vector3.Lerp(b, c, t);
        var cd = Vector3.Lerp(c, d, t);
        var abc = Vector3.Lerp(ab, bc, t);
        var bcd = Vector3.Lerp(bc, cd, t);
        var point = Vector3.Lerp(abc, bcd, t);
        var first = s.Knots[segment];
        var last = s.Knots[(segment + 1) % s.Knots.Count];
        // Freeze both adjacent handles, including collapsed corner handles, before changing modes.
        if (first.Mode == SplineKnot.Corner)
            first.Incoming = new();
        if (last.Mode == SplineKnot.Corner)
            last.Outgoing = new();
        first.Mode = last.Mode = SplineKnot.Free;
        first.Outgoing = Spatial(ab - a);
        last.Incoming = Spatial(cd - d);
        var knot = new SplineKnot
        {
            Position = Spatial(point),
            Incoming = Spatial(abc - point),
            Outgoing = Spatial(bcd - point),
            Mode = SplineKnot.Free,
        };
        s.Knots.Insert(segment + 1, knot);
        return knot;
    }

    public static void SetHandle(SplineKnot knot, bool incoming, Vector3 offset)
    {
        if (knot.Mode is SplineKnot.Corner or SplineKnot.Auto)
            knot.Mode = SplineKnot.Aligned;
        if (incoming)
            knot.Incoming = Spatial(offset);
        else
            knot.Outgoing = Spatial(offset);
        if (knot.Mode != SplineKnot.Aligned || offset.LengthSquared() < 1e-12f)
            return;
        var other = incoming ? knot.Outgoing : knot.Incoming;
        var aligned = -Vector3.Normalize(offset) * Vector(other).Length();
        if (incoming)
            knot.Outgoing = Spatial(aligned);
        else
            knot.Incoming = Spatial(aligned);
    }

    public static void Smooth(SpatialSpline s, int index, float strength)
    {
        var knot = s.Knots[index];
        var p = Vector(knot.Position);
        var prev =
            index > 0 ? Vector(s.Knots[index - 1].Position)
            : s.Closed ? Vector(s.Knots[^1].Position)
            : p;
        var next =
            index + 1 < s.Knots.Count ? Vector(s.Knots[index + 1].Position)
            : s.Closed ? Vector(s.Knots[0].Position)
            : p;
        var direction = next - prev;
        direction = direction.LengthSquared() > 1e-12f ? Vector3.Normalize(direction) : Vector3.Zero;
        var amount = Math.Clamp(strength, 0, 1) / 3;
        knot.Incoming = Spatial(-direction * Vector3.Distance(prev, p) * amount);
        knot.Outgoing = Spatial(direction * Vector3.Distance(p, next) * amount);
        knot.Mode = SplineKnot.Auto;
    }

    public static void Reverse(SpatialSpline s)
    {
        s.Knots.Reverse();
        foreach (var k in s.Knots)
            (k.Incoming, k.Outgoing) = (k.Outgoing, k.Incoming);
    }

    /// <summary>Replace a shaping corner with a tangent-continuous quadratic arc expressed as a cubic.</summary>
    public static bool RoundCorner(SpatialSpline s, int index, float strength)
    {
        if (
            s.Knots.Count < 3
            || s.Knots.Count >= MaxKnots
            || s.Knots[index].AnchorId.Length > 0
            || (!s.Closed && (index == 0 || index == s.Knots.Count - 1))
        )
            return false;
        var knot = s.Knots[index];
        var p = Vector(knot.Position);
        var before = Vector(s.Knots[(index + s.Knots.Count - 1) % s.Knots.Count].Position);
        var after = Vector(s.Knots[(index + 1) % s.Knots.Count].Position);
        var aLength = Vector3.Distance(p, before);
        var bLength = Vector3.Distance(p, after);
        if (aLength < .001f || bLength < .001f)
            return false;
        var radius = Math.Min(aLength, bLength) * .45f * Math.Clamp(strength, 0, 1);
        if (radius < .001f)
            return false;
        var a = p + (before - p) * (radius / aLength);
        var b = p + (after - p) * (radius / bLength);
        knot.Position = Spatial(a);
        knot.Incoming = new();
        knot.Outgoing = Spatial((p - a) * (2f / 3));
        knot.Mode = SplineKnot.Free;
        s.Knots.Insert(
            index + 1,
            new()
            {
                Position = Spatial(b),
                Incoming = Spatial((p - b) * (2f / 3)),
                Mode = SplineKnot.Free,
            }
        );
        return true;
    }

    public static string Error(SpatialSpline? s)
    {
        if (s == null)
            return "";
        if (s.Knots == null || s.Knots.Count > MaxKnots)
            return "Spline has too many knots or a missing knot list.";
        if (!float.IsFinite(s.Strength) || s.Strength < 0 || s.Strength > 1)
            return "Spline strength must be between 0 and 1.";
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var k in s.Knots)
        {
            if (k == null || string.IsNullOrEmpty(k.Id) || !ids.Add(k.Id) || k.AnchorId == null)
                return "Spline knots require unique identities.";
            if (k.Position?.Finite != true || k.Incoming?.Finite != true || k.Outgoing?.Finite != true)
                return "Spline coordinates must be finite.";
            if (k.Mode is not (SplineKnot.Corner or SplineKnot.Auto or SplineKnot.Aligned or SplineKnot.Free))
                return "Unknown spline handle mode.";
        }
        return "";
    }
}
