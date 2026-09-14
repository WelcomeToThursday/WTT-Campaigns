using System.Collections;
using System.Diagnostics;
using System.Reflection;
using UnityEngine;

namespace WTT.Campaigns.Client.Authoring;

// These switches follow the player's colliders, not the rendering camera. Own
// only their explicit target lists, including the inverse-independent list.
internal sealed class EditorTriggerVisibility : IDisposable
{
    private static readonly FieldInfo Worker = WorkerField("_setComponentsEnabledWorker");
    private static readonly FieldInfo InverseWorker = WorkerField("_setComponentsEnabledWorker2");
    private readonly HashSet<DisablerCullingObject> _switches = new();
    private readonly Queue<DisablerCullingObject> _pendingOwners = new();
    private readonly HashSet<DisablerCullingObject> _queuedOwners = new();
    private readonly Dictionary<Component, bool> _components = new();
    private readonly Dictionary<GameObject, bool> _objects = new();
    private readonly Func<GameObject, bool> _hidden;
    private DisablerCullingObject? _activeOwner;
    private TargetList _activeList;
    private int _activeIndex;
    private List<Component>? _componentsSnapshot;
    private List<Component>? _inverseComponentsSnapshot;
    private List<GameObject>? _gameObjectsSnapshot;
    private int _componentsSnapshotCount;
    private int _inverseComponentsSnapshotCount;
    private int _gameObjectsSnapshotCount;
    private bool _rescanRequested;
    private bool _advancing;
    private bool _disposed;
    internal int SwitchCount => _switches.Count;
    internal int ComponentCount => _components.Count;
    internal int ObjectCount => _objects.Count;
    internal int RevealedCount { get; private set; }
    internal bool Pending => _activeOwner is not null || _pendingOwners.Count != 0;

    internal EditorTriggerVisibility(Func<GameObject, bool> hidden) => _hidden = hidden;

    internal void Observe(DisablerCullingObject owner)
    {
        if (_disposed || !owner)
            return;

        _switches.Add(owner);
        // A previously started hide coroutine would otherwise undo the
        // override on later frames, after the editor has already opened.
        Stop(owner, Worker);
        Stop(owner, InverseWorker);
        if (_queuedOwners.Add(owner))
            _pendingOwners.Enqueue(owner);
        else if (ReferenceEquals(_activeOwner, owner) && ListsChanged())
            _rescanRequested = true;
    }

    internal void Advance(int maxOperations = 4096, double budgetMilliseconds = 2)
    {
        if (_disposed || _advancing || maxOperations <= 0 || double.IsNaN(budgetMilliseconds) || budgetMilliseconds <= 0)
            return;

        var started = Stopwatch.GetTimestamp();
        var unlimited = double.IsPositiveInfinity(budgetMilliseconds);
        var operations = 0;
        _advancing = true;
        try
        {
            while (!_disposed && operations < maxOperations && (unlimited || ElapsedMilliseconds(started) < budgetMilliseconds))
            {
                if (_activeOwner is null)
                {
                    if (!_pendingOwners.TryDequeue(out var owner))
                        break;
                    BeginOwner(owner);
                    operations++;
                    if (!_activeOwner || !PrepareOwnerForNextTarget())
                        CompleteOwner();
                    continue;
                }

                if (!PrepareOwnerForNextTarget())
                {
                    CompleteOwner();
                    operations++;
                    continue;
                }

                TakeAvailableTarget(out var component, out var gameObject);
                if (component is not null)
                    Capture(component);
                else
                    Capture(gameObject);
                operations++;

                // Normalize now so the final target also releases its owner
                // in this call. A list may have grown while the target was
                // being applied, so this intentionally re-reads its Count.
                if (!_disposed && !PrepareOwnerForNextTarget())
                    CompleteOwner();
            }
        }
        finally
        {
            _advancing = false;
        }
    }

    private enum TargetList
    {
        Components,
        InverseComponents,
        GameObjects,
        Complete,
    }

    private static double ElapsedMilliseconds(long started) =>
        (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency;

    private void BeginOwner(DisablerCullingObject owner)
    {
        _activeOwner = owner;
        _activeList = TargetList.Components;
        _activeIndex = 0;
        _rescanRequested = false;
        if (!owner)
            return;
        SnapshotLists();
    }

    private void SnapshotLists()
    {
        var owner = _activeOwner!;
        _componentsSnapshot = owner._componentsToTurnOff;
        _inverseComponentsSnapshot = owner._compsToTurnOffWhoIgnoreInversedColliders;
        _gameObjectsSnapshot = owner._gameObjectsToTurnOff;
        _componentsSnapshotCount = _componentsSnapshot?.Count ?? -1;
        _inverseComponentsSnapshotCount = _inverseComponentsSnapshot?.Count ?? -1;
        _gameObjectsSnapshotCount = _gameObjectsSnapshot?.Count ?? -1;
    }

    private bool ListsChanged()
    {
        var owner = _activeOwner;
        return owner is not null
            && (
                !ReferenceEquals(_componentsSnapshot, owner._componentsToTurnOff)
                || _componentsSnapshotCount != (owner._componentsToTurnOff?.Count ?? -1)
                || !ReferenceEquals(_inverseComponentsSnapshot, owner._compsToTurnOffWhoIgnoreInversedColliders)
                || _inverseComponentsSnapshotCount != (owner._compsToTurnOffWhoIgnoreInversedColliders?.Count ?? -1)
                || !ReferenceEquals(_gameObjectsSnapshot, owner._gameObjectsToTurnOff)
                || _gameObjectsSnapshotCount != (owner._gameObjectsToTurnOff?.Count ?? -1)
            );
    }

    private void ResetOwnerCursor()
    {
        _activeList = TargetList.Components;
        _activeIndex = 0;
        SnapshotLists();
        _rescanRequested = false;
    }

    private bool PrepareOwnerForNextTarget()
    {
        if (_activeOwner is null || !_activeOwner)
            return false;
        if (_rescanRequested || ListsChanged())
            ResetOwnerCursor();
        return MoveToAvailableTarget();
    }

    private bool MoveToAvailableTarget()
    {
        while (_activeOwner is not null && _activeOwner)
        {
            switch (_activeList)
            {
                case TargetList.Components:
                    if (_activeOwner._componentsToTurnOff != null && _activeIndex < _activeOwner._componentsToTurnOff.Count)
                        return true;
                    _activeList = TargetList.InverseComponents;
                    _activeIndex = 0;
                    continue;
                case TargetList.InverseComponents:
                    if (
                        _activeOwner._compsToTurnOffWhoIgnoreInversedColliders != null
                        && _activeIndex < _activeOwner._compsToTurnOffWhoIgnoreInversedColliders.Count
                    )
                        return true;
                    _activeList = TargetList.GameObjects;
                    _activeIndex = 0;
                    continue;
                case TargetList.GameObjects:
                    if (_activeOwner._gameObjectsToTurnOff != null && _activeIndex < _activeOwner._gameObjectsToTurnOff.Count)
                        return true;
                    _activeList = TargetList.Complete;
                    _activeIndex = 0;
                    continue;
                default:
                    return false;
            }
        }
        return false;
    }

    private void TakeAvailableTarget(out Component? component, out GameObject? gameObject)
    {
        component = null;
        gameObject = null;
        var owner = _activeOwner!;
        switch (_activeList)
        {
            case TargetList.Components:
                component = owner._componentsToTurnOff![_activeIndex++];
                break;
            case TargetList.InverseComponents:
                component = owner._compsToTurnOffWhoIgnoreInversedColliders![_activeIndex++];
                break;
            case TargetList.GameObjects:
                gameObject = owner._gameObjectsToTurnOff![_activeIndex++];
                break;
            default:
                throw new InvalidOperationException("No trigger visibility target is available.");
        }
    }

    private void CompleteOwner()
    {
        if (_activeOwner is not null)
            _queuedOwners.Remove(_activeOwner);
        _activeOwner = null;
        _activeList = TargetList.Complete;
        _activeIndex = 0;
        _componentsSnapshot = null;
        _inverseComponentsSnapshot = null;
        _gameObjectsSnapshot = null;
        _componentsSnapshotCount = -1;
        _inverseComponentsSnapshotCount = -1;
        _gameObjectsSnapshotCount = -1;
        _rescanRequested = false;
    }

    private static FieldInfo WorkerField(string name) =>
        typeof(DisablerCullingObject).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(DisablerCullingObject).FullName, name);

    private static void Stop(DisablerCullingObject owner, FieldInfo field)
    {
        if (field.GetValue(owner) is not IEnumerator worker)
            return;
        owner.StopCoroutine(worker);
        field.SetValue(owner, null);
    }

    private void Capture(Component? target)
    {
        if (!target || _components.ContainsKey(target))
            return;
        var enabled = target.IsEnabledUniversal();
        _components.Add(target, enabled);
        if (!enabled && target.SetEnabledUniversal(true))
            RevealedCount++;
    }

    private void Capture(GameObject? target)
    {
        if (!target || _objects.ContainsKey(target))
            return;
        _objects.Add(target, target.activeSelf);
        if (!target.activeSelf && !_hidden(target) && !_disposed)
        {
            target.SetActive(true);
            RevealedCount++;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _pendingOwners.Clear();
        _queuedOwners.Clear();
        _activeOwner = null;
        _componentsSnapshot = null;
        _inverseComponentsSnapshot = null;
        _gameObjectsSnapshot = null;
        _rescanRequested = false;
        // Release before the baked-group lease: baked culling gets the final
        // renderer decision when both systems own the same renderer.
        try
        {
            foreach (var entry in _components)
                if (entry.Key)
                    try
                    {
                        entry.Key.SetEnabledUniversal(entry.Value);
                    }
                    catch (Exception error)
                    {
                        Plugin.Error(error);
                    }
            foreach (var entry in _objects)
                if (entry.Key)
                    try
                    {
                        if (!_hidden(entry.Key))
                            entry.Key.SetActive(entry.Value);
                    }
                    catch (Exception error)
                    {
                        Plugin.Error(error);
                    }
            foreach (var owner in _switches)
                if (owner)
                    try
                    {
                        owner.ForceUpdate();
                    }
                    catch (Exception error)
                    {
                        Plugin.Error(error);
                    }
        }
        finally
        {
            _components.Clear();
            _objects.Clear();
            _switches.Clear();
        }
    }
}
