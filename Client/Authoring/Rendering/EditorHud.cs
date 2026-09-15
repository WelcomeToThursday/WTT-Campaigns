using UnityEngine;

namespace WTT.Campaigns.Client.Authoring.Rendering;

// Runs immediately before canvas rendering so native fades cannot reveal editor HUD.
internal sealed class EditorHud : IDisposable
{
    private sealed class State
    {
        internal readonly CanvasGroup Group;
        private readonly bool _interactable,
            _raycasts,
            _ignoreParents;
        private readonly float _alpha;

        internal State(CanvasGroup group)
        {
            Group = group;
            _alpha = group.alpha;
            _interactable = group.interactable;
            _raycasts = group.blocksRaycasts;
            _ignoreParents = group.ignoreParentGroups;
        }

        internal void Hide()
        {
            if (!Group)
                return;
            Group.alpha = 0;
            Group.interactable = false;
            Group.blocksRaycasts = false;
            Group.ignoreParentGroups = false;
        }

        internal void Restore()
        {
            if (!Group)
                return;
            Group.alpha = _alpha;
            Group.interactable = _interactable;
            Group.blocksRaycasts = _raycasts;
            Group.ignoreParentGroups = _ignoreParents;
            // Native UI transitions can keep a reference to this group in a
            // running VisualExtensions fade coroutine. Destroying an editor-
            // created group during a transition leaves that coroutine with a
            // missing Unity object and produces CanvasGroup.get_alpha errors on
            // its next tick. Keep the small editor-owned component reusable;
            // Dispose restores its state and the target's native lifecycle can
            // remove it with the target object.
        }
    }

    private readonly Dictionary<GameObject, State> _states = new();
    private readonly List<CanvasGroup> _groups = new();

    internal void Suppress(Component root)
    {
        if (!root)
            return;
        SuppressObject(root.gameObject);
        root.GetComponentsInChildren(true, _groups);
        // Include children that otherwise ignore their hidden parent's CanvasGroup.
        foreach (var group in _groups)
            if (group)
                SuppressObject(group.gameObject);
        _groups.Clear();
    }

    private void SuppressObject(GameObject target)
    {
        if (!target)
            return;
        if (!_states.TryGetValue(target, out var state) || !state.Group)
        {
            // Reuse the native group. AddComponent can return null; never dereference
            // it unchecked or repeatedly add groups to an already animated panel.
            var group = target.GetComponent<CanvasGroup>();
            var owned = !group;
            if (owned)
                group = target.AddComponent<CanvasGroup>();
            if (!group)
                return;
            _states[target] = state = new State(group);
        }
        state.Hide();
    }

    public void Dispose()
    {
        foreach (var state in _states.Values)
            state.Restore();
        _states.Clear();
        _groups.Clear();
    }
}
