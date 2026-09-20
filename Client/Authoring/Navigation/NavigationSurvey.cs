using System.Diagnostics;
using System.Text;
using Cysharp.Threading.Tasks;
using EFT;
using EFT.Interactive;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace WTT.Campaigns.Client.Authoring.Navigation;

internal sealed class NavigationSurvey
{
    internal sealed class Issue
    {
        internal Transform? Target;
        internal Mesh? UnreadableMesh;
        internal string Path = "",
            Reason = "";
    }

    internal readonly List<NavMeshBuildSource> Sources = new();
    internal readonly List<Issue> Issues = new();
    private readonly HashSet<Transform> _issueTargets = new();
    internal readonly HashSet<Collider> BakedColliders = new();
    internal readonly List<NavMeshSurface> Surfaces = new();
    internal readonly List<NavigationTerrainTrees.Audit> TreeCoverage = new();

    internal sealed class ConvexExclusion
    {
        internal MeshCollider Collider = null!;
        internal Mesh Mesh = null!;
        internal Bounds PhysicalBounds;
        internal Matrix4x4 Transform;
        public string Path = "",
            Bounds = "",
            Restriction = "";
    }

    internal readonly List<ConvexExclusion> ConvexExclusions = new();
    internal NavMeshBuildSettings Settings;
    internal Bounds Bounds;
    internal bool Local;
    internal int Collected,
        Excluded,
        Meshes,
        Terrains,
        Primitives,
        Recovered,
        Links,
        LegacyLinks,
        Colliders;
    internal long CollectionMs,
        TotalMs;
    internal string OwnershipError = "";

    internal string Summary()
    {
        var s = Settings;
        return $"{(Local ? "LOCAL DIAGNOSTIC" : "FULL MAP")} · {Sources.Count} bake sources / {Collected} collected · {Excluded} excluded\n"
            + $"Meshes {Meshes} · terrain {Terrains} · primitives {Primitives} · loaded solid colliders {Colliders}\n"
            + $"Recovered mesh sources {Recovered}\n"
            + $"Terrain-tree audits {TreeCoverage.Count} (details in navmesh report)\n"
            + $"Conservative convex exclusions {ConvexExclusions.Count} (non-walkable bounds, not recovered hulls)\n"
            + $"Native surfaces {Surfaces.Count} · links {Links} · legacy links {LegacyLinks} · source issues {Issues.Count}\n"
            + $"Infantry agent {s.agentTypeID}: radius {s.agentRadius:0.###}, height {s.agentHeight:0.###}, slope {s.agentSlope:0.#}, step {s.agentClimb:0.###}\n"
            + $"Bounds centre {Bounds.center}, size {Bounds.size}\nCollection {CollectionMs} ms · total scan {TotalMs} ms\n"
            + (
                OwnershipError.Length > 0
                    ? "Source settings notice: " + OwnershipError
                    : "Native registrations remain loaded; only authored additions and cuts are previewed."
            )
            + (Issues.Count > 0 ? "\nIncomplete geometry: use navmesh issues and navmesh select <number>." : "")
            + "\nTactical cover and AI zones are not regenerated. Timings are measurements, not supported-map certification.";
    }

    internal static async UniTask<NavigationSurvey> Collect(
        Bounds? region,
        CancellationToken token,
        NavigationMeshRecovery? recovery = null,
        Action<string>? progress = null,
        WTT.Campaigns.Shared.Spatial.MapNavigationRecipe? recipe = null
    )
    {
        if (!region.HasValue)
            throw new InvalidOperationException("A painted region is required. Whole-map collection was removed.");
        var result = new NavigationSurvey { Local = true };
        var timer = Stopwatch.StartNew();
        progress?.Invoke("Verifying native terrain collision evidence…");
        await NavigationTerrainEvidence.Verify(token);
        // EFT's unfiltered path queries use the default infantry agent (0).
        result.Settings = NavMesh.GetSettingsByID(0);
        if (result.Settings.agentTypeID != 0 || result.Settings.agentRadius <= 0 || result.Settings.agentHeight <= 0)
            throw new InvalidOperationException("No valid native default infantry build settings are available.");
        result.Settings = NavigationSettingsAdapter.Apply(result.Settings, recipe);
        var layerMask = 0;
        foreach (var surface in NavMeshSurface.activeSurfaces.ToArray())
        {
            if (!surface || !surface.isActiveAndEnabled || !surface.navMeshData)
                continue;
            result.Surfaces.Add(surface);
            layerMask |= surface.layerMask;
            if (surface.agentTypeID != 0)
                result.OwnershipError = "Multiple agent types require a separate ownership adapter.";
            if (surface.transform.lossyScale != Vector3.one)
                result.OwnershipError = "A native navigation surface has a scaled transform.";
            if (surface.defaultArea != 0)
                result.OwnershipError = "A native surface overrides its default area; a per-surface collection adapter is required.";
        }
        if (result.Surfaces.Count == 0)
        {
            layerMask = Physics.DefaultRaycastLayers;
            result.OwnershipError = "No owned NavMeshSurface registrations were found; embedded or custom registrations are unsupported.";
        }
        if (NavMeshSurface.ModifyColliders != null)
            result.OwnershipError = "The game has a custom source modifier; its geometry needs a dedicated adapter.";
        var markups = new List<NavMeshBuildMarkup>();
        foreach (var modifier in NavMeshModifier.activeModifiers)
            if (modifier && modifier.isActiveAndEnabled && modifier.AffectsAgentType(0))
                markups.Add(
                    new NavMeshBuildMarkup
                    {
                        root = modifier.transform,
                        overrideArea = modifier.overrideArea,
                        area = modifier.area,
                        ignoreFromBuild = modifier.ignoreFromBuild,
                        applyToChildren = modifier.applyToChildren,
                        // Preserve explicit native links; do not invent jump/drop behaviour.
                        overrideGenerateLinks = true,
                        generateLinks = false,
                    }
                );
        var collected = new List<NavMeshBuildSource>();
        progress?.Invoke("Collecting native collision sources…");
        token.ThrowIfCancellationRequested();
        var collectTimer = Stopwatch.StartNew();
        if (region.HasValue)
            NavMeshBuilder.CollectSources(
                region.Value,
                layerMask,
                NavMeshCollectGeometry.PhysicsColliders,
                0,
                false,
                markups,
                false,
                collected
            );
        result.CollectionMs = collectTimer.ElapsedMilliseconds;
        result.Collected = collected.Count;
        var hasBounds = false;
        var index = 0;
        var slice = Stopwatch.StartNew();
        foreach (var collectedSource in collected)
        {
            var source = collectedSource;
            if (++index % 128 == 0)
            {
                progress?.Invoke($"Checking collision sources {index}/{collected.Count}…");
                if (slice.ElapsedMilliseconds >= 2 || index % 4096 == 0)
                {
                    await UniTask.NextFrame(cancellationToken: token);
                    slice.Restart();
                }
            }
            var component = source.component;
            if (component && Exclude(component.transform))
            {
                result.Excluded++;
                continue;
            }
            var collider = component as Collider ?? (component ? component.GetComponent<TerrainCollider>() : null);
            if (collider && (!collider!.enabled || collider.isTrigger || !collider.gameObject.activeInHierarchy))
            {
                result.Excluded++;
                continue;
            }
            if (collider && collider!.attachedRigidbody && !collider.attachedRigidbody.isKinematic)
            {
                result.AddIssue(component, "Moving rigid body requires a dynamic navigation adapter.");
                continue;
            }
            if (source.sourceObject is Mesh mesh)
            {
                result.Meshes++;
                if (!mesh.isReadable)
                {
                    var copy = region.HasValue || recovery?.Full == true ? recovery?.Find(mesh) : null;
                    if (!copy)
                    {
                        var reason =
                            recovery != null ? recovery.UnresolvedReason(mesh) + ": " + mesh.name : "Mesh is not readable: " + mesh.name;
                        result.AddIssue(component, reason, mesh);
                        continue;
                    }
                    source.sourceObject = copy;
                    result.Recovered++;
                }
            }
            else if (source.sourceObject is TerrainData)
            {
                result.Terrains++;
            }
            else
                result.Primitives++;
            if (!component || !collider)
            {
                result.AddIssue(component, "Collected source has no supported collider owner.");
                continue;
            }
            var bounds = collider!.bounds;
            if (!Finite(bounds.center) || !Finite(bounds.size))
            {
                result.AddIssue(component, "Collider bounds are not finite.");
                continue;
            }
            if (hasBounds)
                result.Bounds.Encapsulate(bounds);
            else
            {
                result.Bounds = bounds;
                hasBounds = true;
            }
            result.BakedColliders.Add(collider);
            result.Sources.Add(source);
        }
        foreach (var collider in result.BakedColliders)
            if (collider is TerrainCollider terrain && terrain.terrainData)
            {
                progress?.Invoke($"Auditing terrain trees: {terrain.name}…");
                result.TreeCoverage.Add(await NavigationTerrainTrees.Collect(result, terrain, region, recovery, token));
            }
        // Audit solids omitted by CollectSources: do not silently certify a mesh with holes.
        index = 0;
        var sceneColliders = UnityEngine.Object.FindObjectsOfType<Collider>();
        slice.Restart();
        foreach (var collider in sceneColliders)
        {
            if (++index % 128 == 0)
            {
                progress?.Invoke($"Checking omitted colliders {index}/{sceneColliders.Length}…");
                if (slice.ElapsedMilliseconds >= 2 || index % 4096 == 0)
                {
                    await UniTask.NextFrame(cancellationToken: token);
                    slice.Restart();
                }
            }
            if (!collider.enabled || collider.isTrigger || Exclude(collider.transform))
                continue;
            if (region.HasValue && !region.Value.Intersects(collider.bounds))
                continue;
            result.Colliders++;
            if ((layerMask & (1 << collider.gameObject.layer)) == 0 || Ignored(collider.transform, markups))
                continue;
            if (!result.BakedColliders.Contains(collider) && !result.HasIssue(collider.transform))
            {
                if (collider is MeshCollider { convex: false } omitted)
                {
                    result.AddOmittedMesh(omitted, recovery, markups);
                    continue;
                }
                if (collider is MeshCollider { convex: true } convex && !convex.attachedRigidbody)
                {
                    result.AddConvexExclusion(convex);
                    continue;
                }
                var obstacle = collider.GetComponentInParent<NavMeshObstacle>();
                var detail =
                    $"Type {collider.GetType().Name}; layer {collider.gameObject.layer}; active parent obstacle {obstacle && obstacle.isActiveAndEnabled}";
                if (collider is MeshCollider meshCollider)
                    detail +=
                        $"; convex {meshCollider.convex}; mesh {(meshCollider.sharedMesh ? meshCollider.sharedMesh.name : "missing")}; readable {meshCollider.sharedMesh && meshCollider.sharedMesh.isReadable}";
                result.AddIssue(collider, "Solid collider was omitted by native source collection. " + detail);
            }
        }
        foreach (var volume in NavMeshModifierVolume.activeModifiers)
        {
            if (!volume || !volume.isActiveAndEnabled || !volume.AffectsAgentType(0) || (layerMask & (1 << volume.gameObject.layer)) == 0)
                continue;
            var scale = volume.transform.lossyScale;
            var size = Vector3.Scale(volume.size, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            result.Sources.Add(
                new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.ModifierBox,
                    transform = Matrix4x4.TRS(volume.transform.TransformPoint(volume.center), volume.transform.rotation, Vector3.one),
                    size = size,
                    area = volume.area,
                    component = volume,
                }
            );
        }
        result.Links = UnityEngine.Object.FindObjectsOfType<NavMeshLink>().Length;
        result.LegacyLinks = UnityEngine.Object.FindObjectsOfType<OffMeshLink>().Length;
        if (region.HasValue)
            result.Bounds = region.Value;
        else if (hasBounds)
            result.Bounds.Expand(result.Settings.agentRadius * 2);
        if (!hasBounds)
            result.OwnershipError = "No readable static collider geometry was collected.";
        result.TotalMs = timer.ElapsedMilliseconds;
        return result;
    }

    private void AddOmittedMesh(MeshCollider collider, NavigationMeshRecovery? recovery, List<NavMeshBuildMarkup> markups)
    {
        // The volume collector can omit a collider whose physical bounds overlap
        // the paint region. Recover its actual triangle mesh, never a renderer or
        // a walkable bounds proxy. Convex cooked hulls use the separate restriction path.
        if (collider.attachedRigidbody)
        {
            AddIssue(collider, "Omitted rigid-body collider requires a dynamic navigation adapter.");
            return;
        }
        foreach (var obstacle in collider.GetComponentsInParent<NavMeshObstacle>())
            if (obstacle.isActiveAndEnabled)
            {
                AddIssue(collider, "Omitted collider belongs to an active navigation obstacle.");
                return;
            }
        foreach (var agent in collider.GetComponentsInParent<NavMeshAgent>())
            if (agent.isActiveAndEnabled)
            {
                AddIssue(collider, "Omitted collider belongs to an active navigation agent.");
                return;
            }
        var original = collider.sharedMesh;
        if (!original)
        {
            AddIssue(collider, "Omitted non-convex collider has no collision mesh.");
            return;
        }
        var matrix = collider.transform.localToWorldMatrix;
        var bounds = collider.bounds;
        if (!SupportedMeshTransform(matrix) || !Finite(bounds.center) || !Finite(bounds.size))
        {
            AddIssue(collider, "Omitted non-convex collider has a non-finite, sheared, mirrored or degenerate transform/bounds.");
            return;
        }
        var mesh = original.isReadable ? original : recovery?.Find(original);
        if (!mesh)
        {
            var reason = recovery == null ? "Mesh is not readable" : recovery.UnresolvedReason(original);
            AddIssue(collider, "Omitted non-convex collider recovery: " + reason + ": " + original.name, original);
            return;
        }
        var area = 0;
        // Match hierarchy precedence: the nearest applicable area override wins.
        for (var at = collider.transform; at; at = at.parent)
        {
            int? overridden = null;
            foreach (var markup in markups)
            {
                if (markup.root != at || !markup.overrideArea || at != collider.transform && !markup.applyToChildren)
                    continue;
                if (overridden.HasValue && overridden.Value != markup.area)
                {
                    AddIssue(collider, "Omitted collider has conflicting navigation area overrides.");
                    return;
                }
                overridden = markup.area;
            }
            if (overridden.HasValue)
            {
                area = overridden.Value;
                break;
            }
        }
        Sources.Add(
            new NavMeshBuildSource
            {
                component = collider,
                shape = NavMeshBuildSourceShape.Mesh,
                sourceObject = mesh,
                transform = matrix,
                area = area,
            }
        );
        BakedColliders.Add(collider);
        Meshes++;
        if (mesh != original)
            Recovered++;
    }

    internal static bool SupportedMeshTransform(Matrix4x4 matrix)
    {
        for (var i = 0; i < 16; i++)
            if (float.IsNaN(matrix[i]) || float.IsInfinity(matrix[i]))
                return false;
        var x = matrix.MultiplyVector(Vector3.right);
        var y = matrix.MultiplyVector(Vector3.up);
        var z = matrix.MultiplyVector(Vector3.forward);
        return x.magnitude >= .00001f
            && y.magnitude >= .00001f
            && z.magnitude >= .00001f
            && Mathf.Abs(Vector3.Dot(x.normalized, y.normalized)) <= .0001f
            && Mathf.Abs(Vector3.Dot(x.normalized, z.normalized)) <= .0001f
            && Mathf.Abs(Vector3.Dot(y.normalized, z.normalized)) <= .0001f
            && Vector3.Dot(Vector3.Cross(x, y), z) > 0;
    }

    private void AddConvexExclusion(MeshCollider collider)
    {
        try
        {
            if (!collider.sharedMesh)
                throw new InvalidOperationException("Convex collider has no collision mesh");
            var physical = collider.bounds;
            var exclusion = NavigationRestrictionGeometry.ConvexExclusion(
                new(physical.center.x, physical.center.y, physical.center.z),
                new(physical.size.x, physical.size.y, physical.size.z),
                Settings.agentRadius,
                Settings.agentHeight,
                Settings.agentClimb,
                Settings.overrideVoxelSize ? Settings.voxelSize : Settings.agentRadius / 3
            );
            var area = NavMesh.GetAreaFromName("Not Walkable");
            if (area < 0)
                throw new InvalidOperationException("Native non-walkable navigation area is unavailable");
            var center = new Vector3(exclusion.Center.X, exclusion.Center.Y, exclusion.Center.Z);
            var size = new Vector3(exclusion.Size.X, exclusion.Size.Y, exclusion.Size.Z);
            Sources.Add(
                new NavMeshBuildSource
                {
                    component = collider,
                    shape = NavMeshBuildSourceShape.ModifierBox,
                    transform = Matrix4x4.TRS(center, Quaternion.identity, Vector3.one),
                    size = size,
                    area = area,
                }
            );
            BakedColliders.Add(collider);
            ConvexExclusions.Add(
                new ConvexExclusion
                {
                    Collider = collider,
                    Mesh = collider.sharedMesh,
                    PhysicalBounds = physical,
                    Transform = collider.transform.localToWorldMatrix,
                    Path = ScenePath(collider.transform),
                    Bounds = physical.ToString("R"),
                    Restriction = new Bounds(center, size).ToString("R"),
                }
            );
        }
        catch (InvalidOperationException error)
        {
            AddIssue(collider, "Omitted convex collider cannot be conservatively excluded: " + error.Message);
        }
    }

    internal bool ConvexExclusionsCurrent()
    {
        foreach (var entry in ConvexExclusions)
            if (
                !entry.Collider
                || !entry.Collider.enabled
                || !entry.Collider.gameObject.activeInHierarchy
                || entry.Collider.isTrigger
                || !entry.Collider.convex
                || entry.Collider.sharedMesh != entry.Mesh
                || entry.Collider.attachedRigidbody
                || entry.Collider.bounds != entry.PhysicalBounds
                || entry.Collider.transform.localToWorldMatrix != entry.Transform
            )
                return false;
        return true;
    }

    private bool HasIssue(Transform target)
    {
        return _issueTargets.Contains(target);
    }

    internal void AddIssue(Component? component, string reason, Mesh? unreadableMesh = null)
    {
        if (component)
            _issueTargets.Add(component!.transform);
        Issues.Add(
            new Issue
            {
                Target = component ? component!.transform : null,
                UnreadableMesh = unreadableMesh,
                Path = component ? ScenePath(component!.transform) : "(unowned source)",
                Reason = reason,
            }
        );
    }

    private static bool Ignored(Transform target, List<NavMeshBuildMarkup> markups)
    {
        foreach (var markup in markups)
            if (markup.ignoreFromBuild && (target == markup.root || markup.applyToChildren && target.IsChildOf(markup.root)))
                return true;
        return false;
    }

    private static bool Exclude(Transform target) =>
        !target.gameObject.scene.IsValid()
        || !target.gameObject.scene.isLoaded
        || !target.gameObject.activeInHierarchy
        || target.GetComponentInParent<Player>()
        || target.GetComponentInParent<LootItem>()
        || target.GetComponentInParent<Door>()
        || target.GetComponentInParent<Canvas>()
        || target.GetComponentInParent<Scenes.SceneNavigationFollower>()
        || HasPreviewAncestor(target);

    private static bool HasPreviewAncestor(Transform target)
    {
        for (var at = target; at; at = at.parent)
            if (at.name is "CampaignEditor visual preview" or "CampaignEditor navigation")
                return true;
        return false;
    }

    internal static string ScenePath(Transform target)
    {
        var parts = new List<string>();
        for (var at = target; at; at = at.parent)
            parts.Add(at.name + "[" + at.GetSiblingIndex() + "]");
        parts.Reverse();
        return target.gameObject.scene.name + ":/" + string.Join("/", parts);
    }

    internal static bool Finite(Vector3 v) =>
        !float.IsNaN(v.x)
        && !float.IsNaN(v.y)
        && !float.IsNaN(v.z)
        && !float.IsInfinity(v.x)
        && !float.IsInfinity(v.y)
        && !float.IsInfinity(v.z);

    internal string IssuesPage(int page)
    {
        var start = (page - 1) * 10;
        var text = new StringBuilder($"Source issues {Issues.Count} · page {page}\n");
        for (var i = start; i < Math.Min(start + 10, Issues.Count); i++)
            text.AppendLine($"{i + 1}. {Issues[i].Reason}\n{Issues[i].Path}");
        return text.Append("Select an object: navmesh select <number>").ToString();
    }

    internal bool SameSources(NavigationSurvey other)
    {
        if (
            Sources.Count != other.Sources.Count
            || Issues.Count != other.Issues.Count
            || Bounds != other.Bounds
            || !Settings.Equals(other.Settings)
            || TreeCoverage.Count != other.TreeCoverage.Count
        )
            return false;
        for (var i = 0; i < TreeCoverage.Count; i++)
            if (TreeCoverage[i].Path != other.TreeCoverage[i].Path || TreeCoverage[i].State != other.TreeCoverage[i].State)
                return false;
        // CollectSources preserves order within this loaded scene; a reordered list forces a rescan/rebake.
        for (var i = 0; i < Sources.Count; i++)
        {
            var a = Sources[i];
            var b = other.Sources[i];
            if (
                a.component != b.component
                || a.sourceObject != b.sourceObject
                || a.shape != b.shape
                || a.transform != b.transform
                || a.size != b.size
                || a.area != b.area
            )
                return false;
        }
        return true;
    }
}
