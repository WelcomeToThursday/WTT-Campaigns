using System.Collections;
using UnityEngine;

// Test doubles for the state lease only; the installed signatures are checked
// separately against Assembly-CSharp by EditorSceneVisibilityChecks.
internal sealed class DisablerCullingObject : Component
{
    public List<Component> _componentsToTurnOff = new();
    public List<Component> _compsToTurnOffWhoIgnoreInversedColliders = new();
    public List<GameObject> _gameObjectsToTurnOff = new();
    private IEnumerator? _setComponentsEnabledWorker;
    private IEnumerator? _setComponentsEnabledWorker2;
    internal int Stopped, Refreshed;
    internal void Workers(IEnumerator first, IEnumerator second)
    {
        _setComponentsEnabledWorker = first;
        _setComponentsEnabledWorker2 = second;
    }
    internal bool Pending => _setComponentsEnabledWorker != null || _setComponentsEnabledWorker2 != null;
    public void StopCoroutine(IEnumerator worker) => Stopped++;
    public void ForceUpdate() => Refreshed++;
}

internal sealed class CullingComponent : Component
{
    internal bool Enabled;
}

internal static class ComponentExtensions
{
    public static bool IsEnabledUniversal(this Component component) => ((CullingComponent)component).Enabled;
    public static bool SetEnabledUniversal(this Component component, bool value)
    {
        var target = (CullingComponent)component;
        var changed = target.Enabled != value;
        target.Enabled = value;
        return changed;
    }
}
