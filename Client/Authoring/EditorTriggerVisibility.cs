using System.Collections;
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
    private readonly Dictionary<Component, bool> _components = new();
    private readonly Dictionary<GameObject, bool> _objects = new();
    private readonly Func<GameObject, bool> _hidden;
    private bool _applying;
    internal int SwitchCount => _switches.Count;
    internal int ComponentCount => _components.Count;
    internal int ObjectCount => _objects.Count;
    internal int RevealedCount { get; private set; }

    internal EditorTriggerVisibility(Func<GameObject, bool> hidden) => _hidden = hidden;

    internal void Observe(DisablerCullingObject owner)
    {
        if (!owner || _applying) return;
        _applying = true;
        try
        {
            _switches.Add(owner);
            // A previously started hide coroutine would otherwise undo the
            // override on later frames, after the editor has already opened.
            Stop(owner, Worker);
            Stop(owner, InverseWorker);
            Capture(owner._componentsToTurnOff);
            Capture(owner._compsToTurnOffWhoIgnoreInversedColliders);
            if (owner._gameObjectsToTurnOff == null) return;
            foreach (var target in owner._gameObjectsToTurnOff)
            {
                if (!target || _objects.ContainsKey(target)) continue;
                _objects.Add(target, target.activeSelf);
                if (!target.activeSelf && !_hidden(target))
                {
                    target.SetActive(true);
                    RevealedCount++;
                }
            }
        }
        finally { _applying = false; }
    }

    private static FieldInfo WorkerField(string name) =>
        typeof(DisablerCullingObject).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(DisablerCullingObject).FullName, name);

    private static void Stop(DisablerCullingObject owner, FieldInfo field)
    {
        if (field.GetValue(owner) is not IEnumerator worker) return;
        owner.StopCoroutine(worker);
        field.SetValue(owner, null);
    }

    private void Capture(List<Component>? targets)
    {
        if (targets == null) return;
        foreach (var target in targets)
        {
            if (!target || _components.ContainsKey(target)) continue;
            var enabled = target.IsEnabledUniversal();
            _components.Add(target, enabled);
            if (!enabled && target.SetEnabledUniversal(true)) RevealedCount++;
        }
    }

    public void Dispose()
    {
        // Release before the baked-group lease: baked culling gets the final
        // renderer decision when both systems own the same renderer.
        foreach (var entry in _components)
            if (entry.Key)
                try { entry.Key.SetEnabledUniversal(entry.Value); }
                catch (Exception error) { Plugin.Error(error); }
        foreach (var entry in _objects)
            if (entry.Key && !_hidden(entry.Key))
                try { entry.Key.SetActive(entry.Value); }
                catch (Exception error) { Plugin.Error(error); }
        foreach (var owner in _switches)
            if (owner) owner.ForceUpdate();
        _components.Clear();
        _objects.Clear();
        _switches.Clear();
    }
}
