using UnityEngine;
using UnityEngine.AI;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Client.Encounters;

// Native registration is a runtime lease. It is never serialized into map data
// and must outlive all owned bots, including failed/cancelled native teardown.
internal sealed class EncounterCorePoints : IDisposable
{
    private readonly List<AICorePoint> _owned = new();
    private AICorePointHolder? _holder;
    private AICoversData? _covers;
    private readonly List<int> _groups = new();

    internal void Prepare(EncounterRuntimeContext context, MapLayout layout)
    {
        if (
            !context.HasIdentity
            || (!context.IsPreview && !(context.Mode == EncounterRuntimeModes.Mission && context.PublishedLayoutConfirmed))
        )
            throw new InvalidOperationException("Local AI cores require an authenticated preview or published mission.");
        if (_holder || _owned.Count != 0)
            throw new InvalidOperationException("Local AI cores are already prepared.");
        var assigned = EncounterCorePlan.Assigned(layout);
        if (assigned.Count == 0)
            return;
        _covers = UnityEngine.Object.FindObjectOfType<AICoversData>();
        _holder = _covers ? _covers!.AICorePointsHolder : null;
        if (!_holder || _holder!.CorePoints == null || _covers!.CorePointsGroupByObjects == null)
            throw new InvalidOperationException("The native AI core registry is not ready.");
        var current = AICorePointHolder.GetAllTestObjects(false);
        if (!ReferenceEquals(current, _holder.CorePoints))
            throw new InvalidOperationException("The native AI core and cover registries belong to different maps.");
        var navigation = new EncounterNavigation();
        foreach (var point in assigned)
            if (!navigation.IsOnNavMesh(point.Position) || !navigation.HasStandingClearance(point.Position))
                throw new InvalidOperationException("Local AI core requires clear walkable ground at spawn '" + point.Name + "'.");
        var anchors = EncounterCorePlan.Anchors(
            assigned,
            point => ConnectedCore(current, EncounterNavigation.ToVector3(point.Position)) != null,
            (a, b) => Reach(EncounterNavigation.ToVector3(a.Position), EncounterNavigation.ToVector3(b.Position))
        );
        var usedIds = new HashSet<int>();
        var usedGroups = new HashSet<int>(_covers.CorePointsGroupByObjects);
        if (_covers.ConnectionsGroupCount != null)
            usedGroups.UnionWith(_covers.ConnectionsGroupCount);
        if (_covers.CorePointsGroup != null)
            usedIds.UnionWith(_covers.CorePointsGroup);
        foreach (var point in current)
            if (point)
            {
                usedIds.Add(point.Id);
                usedGroups.Add(point.ConnectionGroupId);
            }
        foreach (var point in UnityEngine.Object.FindObjectsOfType<AICorePoint>())
        {
            usedIds.Add(point.Id);
            usedGroups.Add(point.ConnectionGroupId);
        }
        try
        {
            foreach (var anchor in anchors)
            {
                var id = EncounterCorePlan.Allocate(usedIds);
                var group = EncounterCorePlan.Allocate(usedGroups);
                var go = new GameObject("Campaign mission AI core");
                go.SetActive(false);
                var point = go.AddComponent<AICorePoint>();
                _owned.Add(point);
                point.ClearConnections();
                point.SetIds(id, group);
                point.transform.position = EncounterNavigation.ToVector3(anchor.Position);
                // AddCorePoint parents into the loaded native holder and registers the ID.
                _holder.AddCorePoint(point);
                point.Restore(_holder.CorePoints);
                _covers.CorePointsGroupByObjects.Add(group);
                _groups.Add(group);
                go.SetActive(true);
                Plugin.LogInfo(
                    $"Mission AI core {id}, group {group}, spawn '{anchor.Name}' at {point.Position}: isolated navigation island; no baked cover added."
                );
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal static bool Reach(Vector3 from, Vector3 to)
    {
        var path = new NavMeshPath();
        return NavMesh.CalculatePath(from, to, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
    }

    internal static AICorePoint? ConnectedCore(IEnumerable<AICorePoint> points, Vector3 position)
    {
        foreach (var point in points)
            if (point && Reach(position, point.Position) && Reach(point.Position, position))
                return point;
        return null;
    }

    public void Dispose()
    {
        foreach (var point in _owned)
        {
            if (_holder)
                _holder!.CorePoints?.Remove(point);
            if (point)
            {
                point.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(point.gameObject);
            }
        }
        if (_covers && _covers!.CorePointsGroupByObjects != null)
            foreach (var group in _groups)
                _covers.CorePointsGroupByObjects.Remove(group);
        _owned.Clear();
        _groups.Clear();
        _holder = null;
        _covers = null;
    }
}
