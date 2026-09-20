using Cysharp.Threading.Tasks;
using EFT;
using UnityEngine;
using UnityEngine.AI;

namespace WTT.Campaigns.Client.Authoring.Navigation;

// Read-only samples of ACTIVE navigation. No bake, carving, repair or registration.
internal sealed class NavigationHealthScan
{
    internal const float Radius = 24;
    internal const int Limit = 4096;
    internal readonly List<Vector3> Vertices = new();
    internal readonly List<int> Indices = new();
    internal readonly List<Vector2> Issues = new();
    internal readonly int[] Counts = new int[6];
    private readonly RaycastHit[] _hits = new RaycastHit[128];
    private readonly Collider[] _overlaps = new Collider[128];
    private bool _saturated;
    internal int Unverified;
    internal int Samples;
    internal bool Truncated;
    internal Vector3 Origin;

    internal static async UniTask<NavigationHealthScan> Run(Vector3 origin, float? floor, CancellationToken token, Action<int> progress)
    {
        var settings = NavMesh.GetSettingsByID(0);
        if (settings.agentRadius <= 0 || settings.agentHeight <= settings.agentRadius * 2)
            throw new InvalidOperationException("Native infantry dimensions are unavailable.");
        var filter = new NavMeshQueryFilter { agentTypeID = settings.agentTypeID, areaMask = NavMesh.AllAreas };
        if (!NavMesh.SamplePosition(origin, out var anchor, .6f, filter))
            throw new InvalidOperationException("Move the camera above active navigation to choose the scan origin.");
        var result = new NavigationHealthScan { Origin = anchor.position };
        var data = NavMesh.CalculateTriangulation();
        var path = new NavMeshPath();
        var stack = new Stack<(Vector3 A, Vector3 B, Vector3 C)>();
        var work = 0;
        var frame = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < data.indices.Length; i += 3)
        {
            stack.Push((data.vertices[data.indices[i]], data.vertices[data.indices[i + 1]], data.vertices[data.indices[i + 2]]));
            while (stack.Count > 0)
            {
                if (++work % 1024 == 0 || frame.ElapsedMilliseconds >= 2)
                {
                    token.ThrowIfCancellationRequested();
                    progress(result.Samples);
                    await UniTask.Yield(PlayerLoopTiming.Update, token);
                    frame.Restart();
                }
                var (a, b, c) = stack.Pop();
                var min = Vector3.Min(a, Vector3.Min(b, c));
                var max = Vector3.Max(a, Vector3.Max(b, c));
                if (max.x < origin.x - Radius || min.x > origin.x + Radius || max.z < origin.z - Radius || min.z > origin.z + Radius)
                    continue;
                if (floor.HasValue && (max.y < floor.Value - .6f || min.y > floor.Value + .6f))
                    continue;
                var ab = (a - b).sqrMagnitude;
                var bc = (b - c).sqrMagnitude;
                var ca = (c - a).sqrMagnitude;
                if (Mathf.Max(ab, Mathf.Max(bc, ca)) > 9)
                {
                    if (ab >= bc && ab >= ca)
                    {
                        var m = (a + b) * .5f;
                        stack.Push((a, m, c));
                        stack.Push((m, b, c));
                    }
                    else if (bc >= ca)
                    {
                        var m = (b + c) * .5f;
                        stack.Push((a, b, m));
                        stack.Push((a, m, c));
                    }
                    else
                    {
                        var m = (c + a) * .5f;
                        stack.Push((a, b, m));
                        stack.Push((m, b, c));
                    }
                    continue;
                }
                if (result.Samples >= Limit)
                {
                    result.Truncated = true;
                    return result;
                }
                var point = (a + b + c) / 3;
                if (new Vector2(point.x - origin.x, point.z - origin.z).sqrMagnitude > Radius * Radius)
                    continue;
                var issue = result.Inspect(point, a, b, c, anchor.position, settings, filter, path);
                var start = result.Vertices.Count;
                result.Vertices.Add(a);
                result.Vertices.Add(b);
                result.Vertices.Add(c);
                for (var k = 0; k < 3; k++)
                {
                    result.Indices.Add(start + k);
                    result.Issues.Add(new Vector2((int)issue, 0));
                }
                if ((issue & NavigationHealthIssue.Unchecked) != 0)
                    result.Unverified++;
                else
                    for (var k = 0; k < 6; k++)
                        if (((int)issue & (1 << k)) != 0)
                            result.Counts[k]++;
                result.Samples++;
            }
        }
        token.ThrowIfCancellationRequested();
        return result;
    }

    private NavigationHealthIssue Inspect(
        Vector3 point,
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 anchor,
        NavMeshBuildSettings settings,
        NavMeshQueryFilter filter,
        NavMeshPath path
    )
    {
        _saturated = false;
        var supported = Support(point, out var ground);
        var gap = supported ? Mathf.Abs(ground.point.y - point.y) : 0;
        // Probe inset corners as well as the centre; a partial support hole is still suspicious.
        var allSupport = supported;
        allSupport &= Support(Vector3.Lerp(point, a, .7f), out _);
        allSupport &= Support(Vector3.Lerp(point, b, .7f), out _);
        allSupport &= Support(Vector3.Lerp(point, c, .7f), out _);
        var bottom = point + Vector3.up * (settings.agentRadius + .05f);
        var top = point + Vector3.up * (settings.agentHeight - settings.agentRadius + .05f);
        var clear = true;
        var overlapCount = Physics.OverlapCapsuleNonAlloc(
            bottom,
            top,
            settings.agentRadius,
            _overlaps,
            Physics.AllLayers,
            QueryTriggerInteraction.Ignore
        );
        _saturated |= overlapCount == _overlaps.Length;
        for (var i = 0; i < overlapCount; i++)
            if (Physical(_overlaps[i]))
            {
                clear = false;
                break;
            }
        Array.Clear(_overlaps, 0, overlapCount);
        var slope = supported
            ? Vector3.Angle(ground.normal, Vector3.up)
            : Vector3.Angle(Vector3.Cross(b - a, c - a).normalized, Vector3.up);
        slope = Mathf.Min(slope, 180 - slope);
        // Opposing physical walls closer than the agent diameter plus 20 cm are a warning.
        // Testing the body height avoids treating the support floor as a narrow passage.
        var narrow = false;
        for (var axis = 0; axis < 4; axis++)
        {
            var direction = new Vector3(Mathf.Cos(axis * Mathf.PI / 4), 0, Mathf.Sin(axis * Mathf.PI / 4));
            var centre = point + Vector3.up * Mathf.Max(.6f, settings.agentHeight * .5f);
            var distance = settings.agentRadius * 2 + .2f;
            var left = WallDistance(centre, direction, distance);
            var right = WallDistance(centre, -direction, distance);
            narrow |= left + right < distance;
        }
        var sampled = NavMesh.SamplePosition(point, out var target, .15f, filter);
        var connected =
            sampled
            && NavMesh.CalculatePath(anchor, target.position, filter, path)
            && path.status == NavMeshPathStatus.PathComplete
            && NavMesh.CalculatePath(target.position, anchor, filter, path)
            && path.status == NavMeshPathStatus.PathComplete;
        var issue = NavigationHealthRules.Classify(
            supported,
            clear,
            connected,
            slope,
            settings.agentSlope,
            narrow,
            gap,
            settings.agentClimb
        );
        if (!allSupport)
            issue |= NavigationHealthIssue.Support;
        return sampled && !_saturated ? issue : issue | NavigationHealthIssue.Unchecked;
    }

    private bool Support(Vector3 point, out RaycastHit support)
    {
        support = default;
        var found = false;
        var closest = float.MaxValue;
        var count = Physics.RaycastNonAlloc(point + Vector3.up, Vector3.down, _hits, 3, Physics.AllLayers, QueryTriggerInteraction.Ignore);
        _saturated |= count == _hits.Length;
        for (var i = 0; i < count; i++)
        {
            var hit = _hits[i];
            if (!Physical(hit.collider) || hit.normal.y <= .05f)
                continue;
            var delta = Mathf.Abs(point.y - hit.point.y);
            if (delta >= closest)
                continue;
            closest = delta;
            support = hit;
            found = true;
        }
        return found;
    }

    private float WallDistance(Vector3 point, Vector3 direction, float limit)
    {
        var distance = limit;
        var count = Physics.RaycastNonAlloc(point, direction, _hits, limit, Physics.AllLayers, QueryTriggerInteraction.Ignore);
        _saturated |= count == _hits.Length;
        for (var i = 0; i < count; i++)
            if (Physical(_hits[i].collider))
                distance = Mathf.Min(distance, _hits[i].distance);
        return distance;
    }

    private static bool Physical(Collider collider) =>
        collider
        && !collider.GetComponentInParent<Player>()
        && !collider.transform.name.StartsWith("CampaignEditor", StringComparison.Ordinal)
        && (!collider.attachedRigidbody || collider.attachedRigidbody.isKinematic);
}
