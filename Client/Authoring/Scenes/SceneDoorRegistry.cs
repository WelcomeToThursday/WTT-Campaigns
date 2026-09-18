using System.Collections;
using System.Reflection;

namespace WTT.Campaigns.Client.Authoring.Scenes;

internal sealed class SceneDoorRegistry
{
    private readonly IDictionary? _entries;

    internal SceneDoorRegistry(object? owner, FieldInfo field, bool localGame)
    {
        // SPT local worlds need no network door registry. Never reflect on a null owner.
        if (owner == null && localGame)
            return;
        _entries = owner == null ? null : field.GetValue(owner) as IDictionary;
        if (_entries == null)
            throw new InvalidOperationException("The native door registry is not ready.");
    }

    internal bool Available => _entries != null;

    internal bool Contains(string id) => _entries?.Contains(id) == true;

    internal void Remove(string id, object door)
    {
        if (_entries != null && ReferenceEquals(_entries[id], door))
            _entries.Remove(id);
    }
}
