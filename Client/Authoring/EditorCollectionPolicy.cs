using UnityEngine.Scripting;

namespace WTT.Campaigns.Client.Authoring;

// Preserve the latest mode requested by the game while editor maps need collection.
internal sealed class EditorCollectionPolicy
{
    private bool _active;
    private GarbageCollector.Mode _restore;

    internal GarbageCollector.Mode? Transition(bool active, GarbageCollector.Mode current)
    {
        if (active == _active)
            return null;
        _active = active;
        if (active)
        {
            _restore = current;
            return GarbageCollector.Mode.Enabled;
        }
        return _restore;
    }

    internal GarbageCollector.Mode Filter(GarbageCollector.Mode requested)
    {
        if (!_active)
            return requested;
        _restore = requested;
        return GarbageCollector.Mode.Enabled;
    }
}
