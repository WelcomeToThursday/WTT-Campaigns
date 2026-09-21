using System.Numerics;
using WTT.Campaigns.Client.Authoring.Navigation;

namespace WTT.Campaigns.Tests;

internal static class NavigationTreeGeometryChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var transform = NavigationTreeGeometry.Placement(new(100, 10, -50), new(200, 40, 300), new(.5f, .25f, .1f), MathF.PI / 2, 2, 3);
        var point = Vector3.Transform(new(1, 2, 0), transform);
        check(
            Vector3.Distance(point, new(200, 26, -22)) < .0001f,
            "Tree placement preserves terrain offset, normalized height, radians and independent width/height"
        );
        var corners = NavigationTreeGeometry.Corners(transform, new(0, 1, 0), new(2, 2, 2));
        check(NavigationTreeGeometry.Intersects(corners, new(200, 23, -20), new(1)), "Tree collider overlapping probe is included");
        check(
            !NavigationTreeGeometry.Intersects(corners, new(200, 100, -20), new(1)),
            "Stacked-floor probe excludes nonoverlapping tree collider"
        );
        check(NavigationTreeGeometry.Intersects(corners, new(202.5f, 23, -20), new(1)), "Tree collider at probe boundary is included");
        var original = NavigationTreeGeometry.Corners(Matrix4x4.Identity, Vector3.Zero, new(2));
        var rotated = NavigationTreeGeometry.Corners(Matrix4x4.CreateRotationY(MathF.PI / 2), Vector3.Zero, new(2));
        check(!NavigationTreeGeometry.Matches(original, rotated), "Matching bounds alone cannot certify a reoriented tree mesh");
        check(NavigationTreeGeometry.MatchesBox(original, rotated), "A quarter-turn of a physical cube preserves its solid geometry");
        check(
            !NavigationTreeGeometry.MatchesBox(
                original,
                NavigationTreeGeometry.Corners(Matrix4x4.CreateRotationY(MathF.PI / 4), Vector3.Zero, new(2))
            ),
            "A box rotated to different corners is not covered"
        );
        var capsule = NavigationTreeGeometry.Corners(Matrix4x4.Identity, Vector3.Zero, new(1, 5, 1));
        var spunCapsule = NavigationTreeGeometry.Corners(Matrix4x4.CreateRotationY(.7f), Vector3.Zero, new(1, 5, 1));
        var reversedCapsule = NavigationTreeGeometry.Corners(Matrix4x4.CreateRotationZ(MathF.PI), Vector3.Zero, new(1, 5, 1));
        check(NavigationTreeGeometry.MatchesRound(capsule, spunCapsule, true), "Capsule coverage ignores rotation about its axis");
        check(NavigationTreeGeometry.MatchesRound(capsule, reversedCapsule, true), "Capsule coverage accepts swapped endcaps");
        check(
            !NavigationTreeGeometry.MatchesRound(
                capsule,
                NavigationTreeGeometry.Corners(Matrix4x4.Identity, Vector3.Zero, new(1, 4, 1)),
                true
            ),
            "Shorter capsule cannot cover a tree collider"
        );
        check(
            !NavigationTreeGeometry.MatchesRound(
                capsule,
                NavigationTreeGeometry.Corners(Matrix4x4.Identity, Vector3.Zero, new(.9f, 5, .9f)),
                true
            ),
            "Narrower capsule cannot cover a tree collider"
        );
        check(
            !NavigationTreeGeometry.MatchesRound(
                capsule,
                NavigationTreeGeometry.Corners(Matrix4x4.CreateRotationZ(.1f), Vector3.Zero, new(1, 5, 1)),
                true
            ),
            "Different capsule axis does not establish coverage"
        );
        check(
            NavigationTreeGeometry.MatchesRound(
                original,
                NavigationTreeGeometry.Corners(Matrix4x4.CreateRotationX(.6f), Vector3.Zero, new(2)),
                false
            ),
            "Sphere coverage is independent of rotation"
        );
        var shearRound = Matrix4x4.Identity;
        shearRound.M12 = .2f;
        check(
            !NavigationTreeGeometry.MatchesRound(capsule, NavigationTreeGeometry.Corners(shearRound, Vector3.Zero, new(1, 5, 1)), true),
            "Sheared primitive is never certified by round-shape matching"
        );
        check(
            NavigationTreeGeometry.Matches(original, original.Select(p => p + new Vector3(.0001f)).ToArray()),
            "Tree source matching tolerates submillimetre native rounding"
        );
        check(
            !NavigationTreeGeometry.Matches(original, original.Select(p => p + new Vector3(.01f, 0, 0)).ToArray()),
            "Nearby collider cannot mask missing tree geometry"
        );
        foreach (var invalid in new[] { float.NaN, float.PositiveInfinity, 0, -1 })
        {
            var rejected = false;
            try
            {
                NavigationTreeGeometry.Placement(Vector3.Zero, Vector3.One, new(.5f), 0, invalid, 1);
            }
            catch (InvalidOperationException)
            {
                rejected = true;
            }
            check(rejected, "Invalid terrain-tree scale is rejected: " + invalid);
        }
        var corrupt = original.ToArray();
        corrupt[0].X = float.NaN;
        check(!NavigationTreeGeometry.Matches(original, corrupt), "Nonfinite source never certifies tree coverage");

        var farTree = Matrix4x4.CreateTranslation(500, 0, 0);
        check(
            NavigationTreeGeometry.OutsideEnvelope(
                Matrix4x4.Identity,
                new(2, 3, 4),
                Vector3.Zero,
                farTree,
                new(0, 5, 0),
                new(2, 10, 2),
                Vector3.Zero,
                new(32, 16, 32)
            ),
            "A remote disabled or transformed prototype cannot block a local 32 m probe"
        );
        check(
            !NavigationTreeGeometry.OutsideEnvelope(
                Matrix4x4.Identity,
                new(100, 1, 1),
                Vector3.Zero,
                Matrix4x4.CreateTranslation(50, 0, 0),
                Vector3.Zero,
                new(2),
                Vector3.Zero,
                new(32, 16, 32)
            ),
            "Large root scale reaching into probe remains unresolved even when tree origin is outside"
        );
        check(
            !NavigationTreeGeometry.OutsideEnvelope(
                Matrix4x4.CreateTranslation(-495, 0, 0),
                Vector3.One,
                Vector3.Zero,
                farTree,
                Vector3.Zero,
                new(2),
                Vector3.Zero,
                new(32, 16, 32)
            ),
            "Child collider offset reaching the probe is not skipped"
        );
        check(
            !NavigationTreeGeometry.OutsideEnvelope(
                Matrix4x4.Identity,
                Vector3.One,
                Vector3.Zero,
                farTree,
                Vector3.Zero,
                new(float.NaN),
                Vector3.Zero,
                new(32, 16, 32)
            ),
            "Unknown prototype bounds remain blockers instead of being treated as outside"
        );
        var random = new Random(4101);
        check(
            Math.Abs(NavigationTreeGeometry.StretchBound(Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ) - 1) < .00001f,
            "Identity transform does not inflate the tree envelope by sqrt(3)"
        );
        check(
            Math.Abs(NavigationTreeGeometry.StretchBound(new(2, 0, 0), new(0, 3, 0), new(0, 0, 4)) - 4) < .00001f,
            "Tree stretch bound uses the maximum nonuniform scale"
        );
        check(
            NavigationTreeGeometry.OutsideEnvelope(
                Matrix4x4.Identity,
                new(2, 1, 2),
                Vector3.Zero,
                Matrix4x4.CreateTranslation(-68.3435f, 21.1289f, -402.0717f),
                new(0, 8, 0),
                new(2, 16, 2),
                new(-56.92f, 21.33f, -333.21f),
                new(32, 16, 32)
            ),
            "A distant tall prototype at the reported tree 106 location is excluded without artificial threefold inflation"
        );
        for (var i = 0; i < 100; i++)
        {
            float Next() => (float)random.NextDouble();
            var child =
                Matrix4x4.CreateScale(.1f + Next() * 3, .1f + Next() * 3, .1f + Next() * 3)
                * Matrix4x4.CreateRotationZ(Next() * 6)
                * Matrix4x4.CreateTranslation(Next() * 20, Next() * 5, Next() * 20);
            var rootScale = new Vector3(.1f + Next() * 5, .1f + Next() * 5, .1f + Next() * 5);
            var rootPosition = new Vector3(Next() * 3, Next() * 3, Next() * 3);
            var root = Matrix4x4.CreateScale(rootScale) * Matrix4x4.CreateRotationX(Next() * 6) * Matrix4x4.CreateTranslation(rootPosition);
            var instance = NavigationTreeGeometry.Placement(
                new(100, 0, -50),
                new(200),
                new(.5f),
                Next() * 6,
                .1f + Next() * 3,
                .1f + Next() * 4
            );
            // Include shear as well as both root-transform conventions.
            var shear = Matrix4x4.Identity;
            shear.M12 = Next() * 2 - 1;
            shear.M23 = Next() * 2 - 1;
            child *= shear;
            foreach (var convention in new[] { child * instance, child * root * instance })
            {
                foreach (var corner in NavigationTreeGeometry.Corners(convention, new(0, 3, 0), new(2, 6, 2)))
                    check(
                        !NavigationTreeGeometry.OutsideEnvelope(
                            child,
                            rootScale,
                            rootPosition,
                            instance,
                            new(0, 3, 0),
                            new(2, 6, 2),
                            corner,
                            new(.1f)
                        ),
                        "Broad phase retains intersecting geometry under either prototype root convention"
                    );
            }
        }
    }
}
