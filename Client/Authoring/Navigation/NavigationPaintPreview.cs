using Cysharp.Threading.Tasks;
using EFT;
using UnityEngine;
using UnityEngine.AI;
using WTT.Campaigns.Client.Authoring.Scenes;
using WTT.Campaigns.Client.Encounters;
using WTT.Campaigns.Client.Spatial;
using WTT.Campaigns.Shared.Spatial;
using ZLinq;

namespace WTT.Campaigns.Client.Authoring.Navigation;

// Additive ownership only. This class never removes or re-registers native data.
internal sealed class NavigationPaintPreview : IDisposable
{
    private readonly Func<NavigationBuildStamp> _stamp;
    private readonly Action<string, bool> _output;
    private readonly List<GameObject> _cuts = new();
    private readonly List<NavMeshLinkInstance> _links = new();
    private NavMeshData? _data;
    private NavMeshData? _owner;
    private NavMeshDataInstance _instance;
    private CancellationTokenSource? _work;
    private NavigationBuildStamp _built;
    private bool _disposed;
    private NavMeshTriangulation _mesh;
    private float? _displayFloor;
    private readonly NavigationSurfaceRenderer _surface = new() { Color = new(.15f, 1f, .35f, .5f) };
    internal bool Busy => _work != null;
    internal bool Active { get; private set; }
    internal string Status { get; private set; } = "Paint Add or Block, then build a preview. Native navigation stays loaded.";

    internal NavigationPaintPreview(Func<NavigationBuildStamp> stamp, Action<string, bool> output)
    {
        _stamp = stamp;
        _output = output;
    }

    internal void Build(MapNavigationRecipe recipe, Func<CancellationToken, UniTask> prepare)
    {
        if (_disposed || Busy || Active)
            throw new InvalidOperationException("Clear the current preview and wait for cleanup before building again.");
        RequireNoBots();
        var errors = MapNavigationRules.Errors(recipe);
        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join("\n", errors));
        var lifetime = _work = new CancellationTokenSource();
        Run(recipe, prepare, lifetime).Forget();
    }

    private async UniTask Run(MapNavigationRecipe recipe, Func<CancellationToken, UniTask> prepare, CancellationTokenSource lifetime)
    {
        var token = lifetime.Token;
        NavMeshData? candidate = null;
        AsyncOperation? operation = null;
        NavigationPaintSources? clipped = null;
        try
        {
            Status = "Preparing the selected layout…";
            _owner = new NavMeshData(0) { name = "CampaignEditor manual navigation owner" };
            await prepare(token);
            RequireNoBots();
            var settings = NavMesh.GetSettingsByID(0);
            if (settings.agentTypeID != 0 || settings.agentRadius <= 0 || settings.agentHeight <= 0)
                throw new InvalidOperationException("Native infantry settings are unavailable.");
            PrepareSupportCaps(recipe, settings);
            var stamp = _stamp();
            for (var i = 0; i < 3; i++)
                await UniTask.NextFrame(cancellationToken: token);
            void Current()
            {
                token.ThrowIfCancellationRequested();
                if (_disposed || !stamp.Equals(_stamp()))
                    throw new OperationCanceledException("Layout changed during manual navigation build.");
            }
            void Progress(string message)
            {
                Current();
                Status = message;
            }
            Current();
            // Additive surfaces must use the same infantry profile as the native map.
            var bounds = NavigationPaintSources.Region(recipe, settings.agentHeight);
            var native = NavMesh.CalculateTriangulation();
            if (recipe.Cells.AsValueEnumerable().Any(c => c.Mode == "Add"))
            {
                using var recovery = new NavigationMeshRecovery();
                var survey = await NavigationSurvey.Collect(bounds, token, progress: Progress);
                Current();
                await recovery.Recover(survey, Progress, token);
                survey = await NavigationSurvey.Collect(bounds, token, recovery, Progress);
                Current();
                if (survey.Issues.Count > 0)
                    throw new InvalidOperationException(
                        "Painted-region geometry is incomplete: " + survey.Issues[0].Reason + " · " + survey.Issues[0].Path
                    );
                clipped = new(recipe, bounds, settings.agentHeight);
                Progress("Clipping physical geometry to Add paint and excluding native navigation…");
                await clipped.Build(survey, native, token);
                Current();
                candidate = new NavMeshData(0) { name = "CampaignEditor manual additions" };
                // Retain small deliberately painted islands; no automatic connections.
                settings.minRegionArea = 0;
                var validation = settings.ValidationReport(bounds);
                if (validation.Length > 0)
                    throw new InvalidOperationException(string.Join("; ", validation));
                operation = NavMeshBuilder.UpdateNavMeshDataAsync(candidate, settings, clipped.Sources, bounds);
                if (operation == null)
                    throw new InvalidOperationException("Navigation builder returned no operation.");
                while (!operation.isDone)
                {
                    Progress($"Building only painted additions · {operation.progress:P0}");
                    await UniTask.NextFrame(cancellationToken: token);
                }
                Current();
                RequireNoBots();
                // Compare registration in one player-loop slice. A snapshot from
                // before recovery/baking can misclassify later native carving as
                // authored additions, including triangles nowhere near the paint.
                var beforeRegistration = NavMesh.CalculateTriangulation();
                if (
                    Difference(beforeRegistration, native).indices.Length != 0
                    || Difference(native, beforeRegistration).indices.Length != 0
                )
                    throw new InvalidOperationException(
                        "Existing navigation changed during the build. Wait for scene obstacles to settle, then rebuild the preview."
                    );
                _instance = NavMesh.AddNavMeshData(candidate);
                if (!_instance.valid)
                    throw new InvalidOperationException("Painted navigation registration failed.");
                try
                {
                    _instance.owner = _owner;
                }
                catch
                {
                    if (_instance.valid && !_instance.owner)
                        _instance.Remove();
                    throw;
                }
                _data = candidate;
                candidate = null;
                var added = Difference(NavMesh.CalculateTriangulation(), beforeRegistration);
                if (added.indices.Length == 0)
                    throw new InvalidOperationException(
                        "No walkable additions were built. Paint a wider supported area outside existing navigation."
                    );
                ValidateFootprint(added, recipe, settings);
                _mesh = added;
                _surface.Capture(added, _displayFloor);
            }
            Current();
            RequireNoBots();
            foreach (var cell in recipe.Cells.AsValueEnumerable().Where(c => c.Mode == "Block"))
            {
                var root = new GameObject("CampaignEditor manual navigation block");
                _cuts.Add(root);
                root.SetActive(false);
                root.transform.position = new Vector3(cell.X + .5f, cell.Y + .05f, cell.Z + .5f);
                var obstacle = root.AddComponent<NavMeshObstacle>();
                obstacle.shape = NavMeshObstacleShape.Box;
                obstacle.size = new Vector3(1, .2f, 1);
                obstacle.carving = true;
                obstacle.carveOnlyStationary = false;
                root.SetActive(true);
            }
            for (var i = 0; i < 3; i++)
                await UniTask.NextFrame(cancellationToken: token);
            Current();
            foreach (var link in recipe.Connections)
            {
                var start = Endpoint(link.Start);
                var end = Endpoint(link.End);
                ValidateConnection(start, end, settings, recipe);
                var instance = NavMesh.AddLink(
                    new NavMeshLinkData
                    {
                        startPosition = start,
                        endPosition = end,
                        width = 0,
                        bidirectional = true,
                        area = 0,
                        agentTypeID = 0,
                        costModifier = -1,
                    }
                );
                if (!instance.valid)
                    throw new InvalidOperationException("A painted connection could not be registered.");
                try
                {
                    instance.owner = _owner;
                }
                catch
                {
                    if (instance.valid && !instance.owner)
                        instance.Remove();
                    throw;
                }
                _links.Add(instance);
            }
            for (var i = 0; i < 2; i++)
                await UniTask.NextFrame(cancellationToken: token);
            Current();
            var navigation = new EncounterNavigation();
            foreach (var link in recipe.Connections)
                if (!navigation.HasCompletePath(link.Start, link.End) || !navigation.HasCompletePath(link.End, link.Start))
                    throw new InvalidOperationException(
                        "A connection failed the two-way physical path check. Clear the endpoint or shorten the connection."
                    );
            _built = stamp;
            Active = true;
            Status =
                $"Manual preview active · {_surface.Triangles:N0} added triangles · {_cuts.Count} block cells · {_links.Count} connections. Native navigation remains registered. Use Observe to test bots.";
            _output(Status, false);
        }
        catch (Exception error)
        {
            RemoveOwned();
            Status =
                error is OperationCanceledException
                    ? "Manual build cancelled; native navigation retained."
                    : "Manual preview failed: " + error.Message;
            _output(Status, error is not OperationCanceledException);
        }
        finally
        {
            if (candidate)
            {
                if (operation != null && !operation.isDone)
                {
                    NavMeshBuilder.Cancel(candidate);
                    while (!operation.isDone)
                        await UniTask.NextFrame();
                }
                UnityEngine.Object.Destroy(candidate);
            }
            clipped?.Dispose();
            if (_work == lifetime)
                _work = null;
            lifetime.Dispose();
        }
    }

    internal void Tick()
    {
        if (Active && (!_built.Equals(_stamp()) || !Owned))
        {
            Clear();
            Status =
                "Manual preview cleared because the layout, scenery or owned registration changed. Paint is saved; rebuild explicitly.";
            _output(Status, false);
        }
    }

    internal void RequireObservation()
    {
        if (!Active || Busy || !_built.Equals(_stamp()) || !Owned)
            throw new InvalidOperationException("Manual navigation preview changed. End Observe and rebuild the preview.");
    }

    internal void Cancel() => _work?.Cancel();

    private bool Owned =>
        _owner
        && (!_data || _instance.valid && _instance.owner == _owner)
        && _links.AsValueEnumerable().All(link => link.valid && link.owner == _owner)
        && _cuts.AsValueEnumerable().All(cut => cut && cut.activeInHierarchy);

    internal void Clear()
    {
        if (Busy)
            throw new InvalidOperationException("Cancel the build and wait for cleanup first.");
        RequireNoBots();
        RemoveOwned();
        Status = "Preview cleared. Saved paint is unchanged; native cuts settle over the next few frames.";
    }

    private void RemoveOwned()
    {
        foreach (var link in _links)
            if (_owner && link.valid && link.owner == _owner)
                link.Remove();
        _links.Clear();
        foreach (var cut in _cuts)
            if (cut)
            {
                cut.SetActive(false);
                UnityEngine.Object.Destroy(cut);
            }
        _cuts.Clear();
        if (_owner && _instance.valid && _instance.owner == _owner)
            _instance.Remove();
        _instance = default;
        if (_data)
            UnityEngine.Object.Destroy(_data);
        _data = null;
        if (_owner)
            UnityEngine.Object.Destroy(_owner);
        _owner = null;
        SceneNavigation.SetPaintSupportCaps(this, null);
        _surface.Clear();
        _mesh = default;
        Active = false;
    }

    internal void Draw(Camera camera) => _surface.Draw(camera);

    internal void DisplayFloor(float? floor)
    {
        _displayFloor = floor;
        _surface.Capture(_mesh, floor);
    }

    public void Dispose()
    {
        _disposed = true;
        Cancel();
        RemoveOwned();
        _surface.Dispose();
    }

    private static void RequireNoBots()
    {
        if (UnityEngine.Object.FindObjectsOfType<BotOwner>().AsValueEnumerable().Any(bot => bot && bot.gameObject.activeInHierarchy))
            throw new InvalidOperationException("End Observe and remove preview bots before changing navigation.");
    }

    private void PrepareSupportCaps(MapNavigationRecipe recipe, NavMeshBuildSettings settings)
    {
        var caps = new Dictionary<Collider, float>();
        var voxel = settings.overrideVoxelSize ? settings.voxelSize : settings.agentRadius / 3;
        foreach (var cell in recipe.Cells.AsValueEnumerable().Where(c => c.Mode == "Add"))
        {
            if (
                !Physics.Raycast(
                    new Vector3(cell.X + .5f, cell.Y + .3f, cell.Z + .5f),
                    Vector3.down,
                    out var hit,
                    .6f,
                    Physics.AllLayers,
                    QueryTriggerInteraction.Ignore
                )
            )
                continue;
            var collider = hit.collider;
            if (collider is not BoxCollider && collider is not MeshCollider)
                continue;
            Bounds local;
            if (collider is BoxCollider box)
                local = new(box.center, box.size);
            else if (collider is MeshCollider mesh && mesh.sharedMesh)
                local = mesh.sharedMesh.bounds;
            else
                continue;
            var normal = collider.transform.InverseTransformDirection(hit.normal);
            var point = collider.transform.InverseTransformPoint(hit.point);
            // Only an actual planar upper support face; never carve guessed ramps
            // from arbitrary mesh bounds or alter the physics collider.
            if (normal.y < .99f || Mathf.Abs(point.y - local.max.y) > .05f)
                continue;
            var scale = Mathf.Abs(collider.transform.lossyScale.y);
            if (scale < .001f)
                continue;
            caps[collider] = point.y - (2 * voxel + .05f) / scale;
        }
        SceneNavigation.SetPaintSupportCaps(this, caps);
    }

    private static Vector3 Endpoint(SpatialVector value)
    {
        var position = ZoneRuntime.Vector(value);
        if (!NavMesh.SamplePosition(position, out var hit, .25f, NavMesh.AllAreas))
            throw new InvalidOperationException(
                "A connection endpoint is not on navigation. Paint a wider entrance and move the endpoint inside it."
            );
        return hit.position;
    }

    private static void ValidateConnection(Vector3 start, Vector3 end, NavMeshBuildSettings settings, MapNavigationRecipe recipe)
    {
        if (Vector3.Distance(start, end) > 5.5f || Mathf.Abs(start.y - end.y) > settings.agentClimb)
            throw new InvalidOperationException(
                "Connections are short ground-level walks only; jumping, stairs and vault links are not supported."
            );
        var delta = end - start;
        var radius = settings.agentRadius;
        var bottom = start + Vector3.up * (radius + .06f);
        var top = start + Vector3.up * Mathf.Max(radius + .06f, settings.agentHeight - radius);
        if (
            Physics.CheckCapsule(bottom, top, radius, Physics.AllLayers, QueryTriggerInteraction.Ignore)
            || Physics.CapsuleCast(
                bottom,
                top,
                radius,
                delta.normalized,
                delta.magnitude,
                Physics.AllLayers,
                QueryTriggerInteraction.Ignore
            )
        )
            throw new InvalidOperationException("The connection crosses a solid obstacle or lacks infantry clearance.");
        for (var distance = 0f; distance <= delta.magnitude + .1f; distance += .1f)
        {
            var point = Vector3.Lerp(start, end, Mathf.Min(1, distance / delta.magnitude));
            if (
                recipe.Cells.AsValueEnumerable().Any(c => c.Mode == "Block" && MapNavigationPainting.Contains(c, point.x, point.y, point.z))
            )
                throw new InvalidOperationException("A connection crosses Block paint. Erase the block or move the connection.");
            if (
                !Physics.Raycast(
                    point + Vector3.up * .2f,
                    Vector3.down,
                    out var hit,
                    .4f,
                    Physics.AllLayers,
                    QueryTriggerInteraction.Ignore
                )
                || hit.normal.y < Mathf.Cos(settings.agentSlope * Mathf.Deg2Rad)
            )
                throw new InvalidOperationException("The connection has no continuous physical floor support.");
        }
    }

    private static (Vector3, Vector3, Vector3) Key(NavMeshTriangulation mesh, int i)
    {
        var a = mesh.vertices[mesh.indices[i]];
        var b = mesh.vertices[mesh.indices[i + 1]];
        var c = mesh.vertices[mesh.indices[i + 2]];
        int Compare(Vector3 x, Vector3 y) =>
            x.x != y.x ? x.x.CompareTo(y.x)
            : x.y != y.y ? x.y.CompareTo(y.y)
            : x.z.CompareTo(y.z);
        if (Compare(a, b) > 0)
            (a, b) = (b, a);
        if (Compare(b, c) > 0)
            (b, c) = (c, b);
        if (Compare(a, b) > 0)
            (a, b) = (b, a);
        return (a, b, c);
    }

    private static NavMeshTriangulation Difference(NavMeshTriangulation current, NavMeshTriangulation native)
    {
        var before = new HashSet<(Vector3, Vector3, Vector3)>();
        for (var i = 0; i < native.indices.Length; i += 3)
            before.Add(Key(native, i));
        var indices = new List<int>();
        for (var i = 0; i < current.indices.Length; i += 3)
            if (!before.Contains(Key(current, i)))
            {
                indices.Add(current.indices[i]);
                indices.Add(current.indices[i + 1]);
                indices.Add(current.indices[i + 2]);
            }
        return new NavMeshTriangulation { vertices = current.vertices, indices = indices.ToArray() };
    }

    private static void ValidateFootprint(NavMeshTriangulation mesh, MapNavigationRecipe recipe, NavMeshBuildSettings settings)
    {
        var footprint = new NavigationPaintFootprint(recipe);
        System.Numerics.Vector3 N(Vector3 p) => new(p.x, p.y, p.z);
        for (var i = 0; i < mesh.indices.Length; i += 3)
        {
            var a = mesh.vertices[mesh.indices[i]];
            var b = mesh.vertices[mesh.indices[i + 1]];
            var c = mesh.vertices[mesh.indices[i + 2]];
            var remaining = footprint.Outside(N(a), N(b), N(c));
            var outsideArea = remaining.AsValueEnumerable().Sum(NavigationPaintGeometry.Area);
            if (outsideArea > .0001f)
            {
                var report = SaveFootprintFailure(recipe, settings, i / 3, N(a), N(b), N(c), remaining, outsideArea);
                throw new InvalidOperationException(
                    $"Built triangle {i / 3} at {((a + b + c) / 3).ToString("F3")} has {outsideArea:0.######} m² outside Add paint's footprint or floor band. "
                        + "Preview rejected; native navigation retained. "
                        + report
                );
            }
        }
    }

    private static string SaveFootprintFailure(
        MapNavigationRecipe recipe,
        NavMeshBuildSettings settings,
        int triangle,
        System.Numerics.Vector3 a,
        System.Numerics.Vector3 b,
        System.Numerics.Vector3 c,
        List<List<System.Numerics.Vector3>> outside,
        float outsideArea
    )
    {
        try
        {
            var directory = Path.Combine(BepInEx.Paths.ConfigPath, "WTT-Campaigns", "navigation-diagnostics");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "paint-footprint-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff") + ".json");
            // Store exact numeric vertices and the recipe so this failure can be
            // replayed by the same pure coverage checker without launching Unity.
            File.WriteAllText(
                path,
                Newtonsoft.Json.JsonConvert.SerializeObject(
                    new
                    {
                        Recipe = recipe,
                        Triangle = triangle,
                        Vertices = new[] { a, b, c },
                        Outside = outside,
                        OutsideArea = outsideArea,
                        AgentRadius = settings.agentRadius,
                        AgentHeight = settings.agentHeight,
                        AgentClimb = settings.agentClimb,
                        VoxelSize = settings.overrideVoxelSize ? settings.voxelSize : settings.agentRadius / 3,
                    },
                    Newtonsoft.Json.Formatting.Indented
                )
            );
            return "Geometry report: " + path;
        }
        catch (Exception error)
        {
            return "Could not save geometry report: " + error.Message;
        }
    }
}
