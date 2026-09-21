using System.Security.Cryptography;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AI;
using NMatrix = System.Numerics.Matrix4x4;
using NVector = System.Numerics.Vector3;

namespace WTT.Campaigns.Client.Authoring.Navigation;

internal static class NavigationTerrainTrees
{
    internal sealed class Audit
    {
        public string Path = "",
            CollisionEvidence = "",
            State = "",
            Result = "";
        public bool? TreeCollidersEnabled;
        public int Instances,
            Outside,
            NonSolid,
            Covered,
            Unresolved;
        public readonly List<object> Problems = new();
        public readonly List<object> Prototypes = new();
        public readonly Dictionary<string, int> ProblemCounts = new();
    }

    internal static async UniTask<Audit> Collect(
        NavigationSurvey survey,
        TerrainCollider terrain,
        Bounds? region,
        NavigationMeshRecovery? recovery,
        CancellationToken token
    )
    {
        var data = terrain.terrainData;
        var treeCollision = NavigationTerrainEvidence.Read(terrain, out var evidence);
        var audit = new Audit
        {
            Path = NavigationSurvey.ScenePath(terrain.transform),
            // This setting is not exposed by EFT's Unity 2022.3 runtime. Unknown
            // must audit the prototypes; it must never be treated as disabled.
            TreeCollidersEnabled = treeCollision,
            CollisionEvidence = evidence,
            Instances = data.treeInstanceCount,
        };
        // These trees have no implicit physics colliders. Any separate scene tree objects
        // still go through the ordinary solid-collider collection and omission audit.
        if (audit.TreeCollidersEnabled == false || audit.Instances == 0)
        {
            audit.State = $"{data.GetInstanceID()}:{audit.TreeCollidersEnabled}:{audit.Instances}";
            audit.Result =
                audit.TreeCollidersEnabled == false
                    ? "Terrain tree collision disabled; scene colliders audited separately"
                    : "No terrain tree instances";
            return audit;
        }
        if (terrain.transform.rotation != Quaternion.identity || terrain.transform.lossyScale != Vector3.one)
        {
            survey.AddIssue(terrain, "Terrain trees on rotated/scaled terrain require a collision adapter.");
            audit.Unresolved = audit.Instances;
            audit.Result = "Unsupported terrain transform";
            audit.State = terrain.transform.localToWorldMatrix.ToString("R");
            return audit;
        }

        var prototypes = data.treePrototypes;
        var trees = data.treeInstances;
        var templates = new Dictionary<int, Collider[]>();
        var fingerprint = new StringBuilder(audit.Path);
        var existing = new Dictionary<(int, int, int, int, int), List<NVector[]>>();
        var sourceIndex = 0;
        foreach (var source in survey.Sources)
        {
            if (++sourceIndex % 128 == 0)
                await UniTask.NextFrame(cancellationToken: token);
            if (Points(source) is { } points)
                Index(existing, source, points);
        }
        var operations = 0;
        for (var i = 0; i < trees.Length; i++)
        {
            if (++operations % 128 == 0)
                await UniTask.NextFrame(cancellationToken: token);
            token.ThrowIfCancellationRequested();
            var tree = trees[i];
            if (tree.prototypeIndex < 0 || tree.prototypeIndex >= prototypes.Length || !prototypes[tree.prototypeIndex].prefab)
            {
                Problem(i, "Missing or invalid tree prototype", null);
                continue;
            }
            var prefab = prototypes[tree.prototypeIndex].prefab;
            if (!templates.TryGetValue(tree.prototypeIndex, out var colliders))
            {
                templates.Add(tree.prototypeIndex, colliders = prefab.GetComponentsInChildren<Collider>(true));
                if (audit.Prototypes.Count < 128)
                    audit.Prototypes.Add(DescribePrototype(tree.prototypeIndex, prefab.transform, colliders));
            }
            NMatrix placement;
            try
            {
                placement = NavigationTreeGeometry.Placement(
                    V(terrain.transform.position),
                    V(data.size),
                    V(tree.position),
                    tree.rotation,
                    tree.widthScale,
                    tree.heightScale
                );
            }
            catch (InvalidOperationException error)
            {
                Problem(i, error.Message, null);
                continue;
            }
            var solids = 0;
            foreach (var collider in colliders)
            {
                if (++operations % 128 == 0)
                    await UniTask.NextFrame(cancellationToken: token);
                // Disabled child branches are not certified as non-solid: Unity terrain's
                // prefab collider extraction can differ from ordinary scene activation.
                if (!collider || collider.isTrigger)
                    continue;
                solids++;
                try
                {
                    var relative = prefab.transform.worldToLocalMatrix * collider.transform.localToWorldMatrix;
                    // Scope comes before capability checks. A disabled or transformed
                    // prototype elsewhere on the terrain cannot invalidate a local probe.
                    // The broad envelope keeps both possible root-transform conventions.
                    if (OutsideProbe(collider, prefab.transform, relative, placement, region))
                    {
                        audit.Outside++;
                        continue;
                    }
                    if (!collider.enabled || !ActiveBranch(collider.transform, prefab.transform))
                        throw new InvalidOperationException("Disabled prototype collider needs native terrain-collision verification");
                    if (prefab.transform.localScale != Vector3.one || prefab.transform.localRotation != Quaternion.identity)
                        throw new InvalidOperationException("Scaled/rotated prototype root needs native terrain-collision verification");
                    var world = M(N(relative) * placement);
                    var expected = ColliderSource(collider, world);
                    expected.component = terrain;
                    var points = Points(expected) ?? throw new InvalidOperationException("Unsupported tree collider shape");
                    fingerprint
                        .Append(i)
                        .Append(':')
                        .Append(tree.prototypeIndex)
                        .Append(':')
                        .Append((int)expected.shape)
                        .Append(':')
                        .Append(expected.sourceObject ? expected.sourceObject.GetInstanceID() : 0)
                        .Append(':')
                        .Append(expected.transform.ToString("R"))
                        .Append(':')
                        .Append(expected.size.ToString("R"));
                    if (region.HasValue && !NavigationTreeGeometry.Intersects(points, V(region.Value.center), V(region.Value.size)))
                    {
                        audit.Outside++;
                        continue;
                    }
                    if (expected.sourceObject is Mesh mesh && !mesh.isReadable)
                    {
                        var copy = recovery?.Find(mesh);
                        if (!copy)
                        {
                            Problem(i, "Unreadable tree collider mesh: " + mesh.name, mesh);
                            continue;
                        }
                        expected.sourceObject = copy;
                    }
                    if (Contains(existing, expected, points))
                        audit.Covered++;
                    else
                        Problem(
                            i,
                            "No matching baked collider for this terrain-tree instance; native collision coverage is unverified",
                            null
                        );
                }
                catch (InvalidOperationException error)
                {
                    Problem(i, error.Message, null);
                }
            }
            if (solids == 0)
                audit.NonSolid++;
        }
        using var sha = SHA256.Create();
        audit.State = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(fingerprint.ToString())));
        audit.Result = audit.Unresolved == 0 ? "Physical terrain-tree coverage accounted for" : "Unresolved physical terrain-tree coverage";
        return audit;

        void Problem(int index, string reason, Mesh? mesh)
        {
            audit.Unresolved++;
            audit.ProblemCounts.TryGetValue(reason, out var count);
            audit.ProblemCounts[reason] = count + 1;
            fingerprint.Append("unresolved:").Append(index).Append(':').Append(reason);
            // One selectable terrain issue is enough to block replacement. Keep detailed,
            // bounded samples in the report, while exposing every distinct recovery mesh.
            if (audit.Problems.Count < 32)
                audit.Problems.Add(
                    new
                    {
                        Tree = index,
                        Prototype = trees[index].prototypeIndex,
                        Position = trees[index].position.ToString("R"),
                        WorldPosition = (terrain.transform.position + Vector3.Scale(data.size, trees[index].position)).ToString("R"),
                        Reason = reason,
                    }
                );
            if (audit.Unresolved == 1 || mesh)
                survey.AddIssue(terrain, $"Terrain tree {index}, prototype {trees[index].prototypeIndex}: {reason}", mesh);
        }
    }

    private static bool OutsideProbe(Collider collider, Transform root, Matrix4x4 relative, NMatrix placement, Bounds? region)
    {
        if (!region.HasValue)
            return false; // Full-map audits still require every solid prototype to be supported.
        if (!LocalBounds(collider, out var center, out var size))
            return false;
        return NavigationTreeGeometry.OutsideEnvelope(
            N(relative),
            V(root.localScale),
            V(root.localPosition),
            placement,
            V(center),
            V(size),
            V(region.Value.center),
            V(region.Value.size)
        );
    }

    private static bool LocalBounds(Collider collider, out Vector3 center, out Vector3 size)
    {
        center = size = Vector3.zero;
        switch (collider)
        {
            case BoxCollider box:
                center = box.center;
                size = box.size;
                break;
            case SphereCollider sphere:
                center = sphere.center;
                size = Vector3.one * (2 * sphere.radius);
                break;
            case CapsuleCollider capsule:
                if (capsule.direction < 0 || capsule.direction > 2)
                    return false;
                center = capsule.center;
                size = Vector3.one * (2 * capsule.radius);
                size[capsule.direction] = Mathf.Max(capsule.height, 2 * capsule.radius);
                break;
            case MeshCollider mesh when mesh.sharedMesh:
                // Mesh bounds are readable metadata even for GPU-only and convex meshes.
                center = mesh.sharedMesh.bounds.center;
                size = mesh.sharedMesh.bounds.size;
                break;
            default:
                return false;
        }
        return true;
    }

    private static object DescribePrototype(int index, Transform root, Collider[] colliders)
    {
        var shapes = new List<object>();
        foreach (var collider in colliders)
        {
            if (!collider)
                continue;
            var knownBounds = LocalBounds(collider, out var center, out var size);
            shapes.Add(
                new
                {
                    Name = collider.name,
                    Type = collider.GetType().Name,
                    collider.enabled,
                    collider.isTrigger,
                    ActiveBranch = ActiveBranch(collider.transform, root),
                    KnownBounds = knownBounds,
                    Center = center.ToString("R"),
                    Size = size.ToString("R"),
                    RelativeTransform = (root.worldToLocalMatrix * collider.transform.localToWorldMatrix).ToString("R"),
                }
            );
        }
        return new
        {
            Index = index,
            Name = root.name,
            Position = root.localPosition.ToString("R"),
            Rotation = root.localRotation.ToString("R"),
            Scale = root.localScale.ToString("R"),
            Colliders = shapes,
        };
    }

    private static bool ActiveBranch(Transform at, Transform root)
    {
        for (; at && at != root; at = at.parent)
            if (!at.gameObject.activeSelf)
                return false;
        return true;
    }

    private static NavMeshBuildSource ColliderSource(Collider collider, Matrix4x4 matrix)
    {
        var x = matrix.MultiplyVector(Vector3.right);
        var y = matrix.MultiplyVector(Vector3.up);
        var z = matrix.MultiplyVector(Vector3.forward);
        if (
            x.magnitude < .00001f
            || y.magnitude < .00001f
            || z.magnitude < .00001f
            || Mathf.Abs(Vector3.Dot(x.normalized, y.normalized)) > .0001f
            || Mathf.Abs(Vector3.Dot(x.normalized, z.normalized)) > .0001f
            || Mathf.Abs(Vector3.Dot(y.normalized, z.normalized)) > .0001f
            || Vector3.Dot(Vector3.Cross(x, y), z) <= 0
        )
            throw new InvalidOperationException("Sheared, mirrored or degenerate tree collider transform");
        var scale = new Vector3(x.magnitude, y.magnitude, z.magnitude);
        var rotation = Quaternion.LookRotation(z, y);
        switch (collider)
        {
            case BoxCollider box:
                return new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Box,
                    transform = Matrix4x4.TRS(matrix.MultiplyPoint3x4(box.center), rotation, Vector3.one),
                    size = Vector3.Scale(box.size, scale),
                };
            case SphereCollider sphere:
                var diameter = 2 * sphere.radius * Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));
                return new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Sphere,
                    transform = Matrix4x4.TRS(matrix.MultiplyPoint3x4(sphere.center), Quaternion.identity, Vector3.one),
                    size = Vector3.one * diameter,
                };
            case CapsuleCollider capsule:
                var direction = capsule.direction;
                var radius = capsule.radius * Mathf.Max(scale[(direction + 1) % 3], scale[(direction + 2) % 3]);
                var height = Mathf.Max(capsule.height * scale[direction], 2 * radius);
                var axis =
                    direction == 0 ? Vector3.right
                    : direction == 2 ? Vector3.forward
                    : Vector3.up;
                return new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Capsule,
                    transform = Matrix4x4.TRS(
                        matrix.MultiplyPoint3x4(capsule.center),
                        rotation * Quaternion.FromToRotation(Vector3.up, axis),
                        Vector3.one
                    ),
                    size = new Vector3(2 * radius, height, 2 * radius),
                };
            case MeshCollider mesh when mesh.sharedMesh && !mesh.convex:
                return new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Mesh,
                    transform = matrix,
                    sourceObject = mesh.sharedMesh,
                };
            default:
                throw new InvalidOperationException("Unsupported or convex tree collider: " + collider.GetType().Name);
        }
    }

    private static NVector[]? Points(NavMeshBuildSource source)
    {
        if (source.shape == NavMeshBuildSourceShape.Mesh && source.sourceObject is Mesh mesh)
            return NavigationTreeGeometry.Corners(N(source.transform), V(mesh.bounds.center), V(mesh.bounds.size));
        if (source.shape is NavMeshBuildSourceShape.Box or NavMeshBuildSourceShape.Capsule or NavMeshBuildSourceShape.Sphere)
            return NavigationTreeGeometry.Corners(N(source.transform), NVector.Zero, V(source.size));
        return null;
    }

    private static (int, int, int, int, int) Key(NavMeshBuildSource source, NVector[] points)
    {
        var center = (points[0] + points[7]) / 2;
        return (
            (int)source.shape,
            source.sourceObject ? source.sourceObject.GetInstanceID() : 0,
            (int)Math.Floor(center.X),
            (int)Math.Floor(center.Y),
            (int)Math.Floor(center.Z)
        );
    }

    private static void Index(Dictionary<(int, int, int, int, int), List<NVector[]>> index, NavMeshBuildSource source, NVector[] points)
    {
        var key = Key(source, points);
        if (!index.TryGetValue(key, out var entries))
            index.Add(key, entries = new List<NVector[]>());
        entries.Add(points);
    }

    private static bool Contains(Dictionary<(int, int, int, int, int), List<NVector[]>> index, NavMeshBuildSource source, NVector[] points)
    {
        var key = Key(source, points);
        for (var x = -1; x <= 1; x++)
        for (var y = -1; y <= 1; y++)
        for (var z = -1; z <= 1; z++)
            if (index.TryGetValue((key.Item1, key.Item2, key.Item3 + x, key.Item4 + y, key.Item5 + z), out var entries))
                foreach (var entry in entries)
                    if (
                        source.shape switch
                        {
                            NavMeshBuildSourceShape.Sphere => NavigationTreeGeometry.MatchesRound(points, entry, false),
                            NavMeshBuildSourceShape.Capsule => NavigationTreeGeometry.MatchesRound(points, entry, true),
                            NavMeshBuildSourceShape.Box => NavigationTreeGeometry.MatchesBox(points, entry),
                            _ => NavigationTreeGeometry.Matches(points, entry),
                        }
                    )
                        return true;
        return false;
    }

    private static NVector V(Vector3 v) => new(v.x, v.y, v.z);

    private static NMatrix N(Matrix4x4 m) =>
        new(m.m00, m.m10, m.m20, m.m30, m.m01, m.m11, m.m21, m.m31, m.m02, m.m12, m.m22, m.m32, m.m03, m.m13, m.m23, m.m33);

    private static Matrix4x4 M(NMatrix m) =>
        new(
            new Vector4(m.M11, m.M12, m.M13, m.M14),
            new Vector4(m.M21, m.M22, m.M23, m.M24),
            new Vector4(m.M31, m.M32, m.M33, m.M34),
            new Vector4(m.M41, m.M42, m.M43, m.M44)
        );
}
