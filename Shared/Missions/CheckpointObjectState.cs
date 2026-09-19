using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Newtonsoft.Json.Linq;

namespace WTT.Campaigns.Shared.Missions;

/// <summary>
/// Raid-local memento for managed native accounting and health state. Preserves object identity,
/// including objects observed by native controllers; never serializes engine objects or resources.
/// </summary>
public sealed class CheckpointObjectState
{
    private sealed class ReferenceComparer : IEqualityComparer<object>
    {
        internal static readonly ReferenceComparer Instance = new();

        public new bool Equals(object? left, object? right) => ReferenceEquals(left, right);

        public int GetHashCode(object value) => RuntimeHelpers.GetHashCode(value);
    }

    private sealed class Entry
    {
        internal object Target = null!;
        internal List<(FieldInfo Field, object? Value)> Fields = new();
        internal object?[]? Items;
        internal List<(object Key, object? Value)>? Pairs;
        internal MethodInfo? SetAdd;
        internal MethodInfo? SetClear;
        internal bool JsonToken;
        internal object? JsonValue;
    }

    private readonly Dictionary<object, Entry> _entries = new(ReferenceComparer.Instance);
    private readonly Func<object, bool> _owns;
    private readonly Func<object, FieldInfo, bool>? _member;

    public CheckpointObjectState(IEnumerable<object> roots, Func<object, bool> owns, Func<object, FieldInfo, bool>? member = null)
    {
        _owns = owns;
        _member = member;
        foreach (var root in roots)
            Capture(root, true);
    }

    public void Restore(IEnumerable<KeyValuePair<object, object>>? replacements = null)
    {
        var map = new Dictionary<object, object>(ReferenceComparer.Instance);
        if (replacements != null)
            foreach (var pair in replacements)
            {
                if (!pair.Key.GetType().IsInstanceOfType(pair.Value))
                    throw new InvalidOperationException("A native checkpoint replacement has an incompatible type.");
                map.Add(pair.Key, pair.Value);
            }
        object? Resolve(object? value, object? current = null)
        {
            if (value == null)
            {
                Delegate? external = null;
                if (current is Delegate existing)
                    foreach (var invocation in existing.GetInvocationList())
                        if (invocation.Target == null || !_owns(invocation.Target))
                            external = Delegate.Combine(external, invocation);
                return external;
            }
            if (map.TryGetValue(value, out var replacement))
                return replacement;
            if (value.GetType().IsValueType && !Scalar(value.GetType()))
            {
                var copy = RuntimeHelpers.GetObjectValue(value);
                foreach (var field in value.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    field.SetValue(copy, Resolve(field.GetValue(value)));
                return copy;
            }
            if (value is Delegate callback)
            {
                Delegate? result = null;
                var live = (current as Delegate)?.GetInvocationList() ?? Array.Empty<Delegate>();
                foreach (var invocation in callback.GetInvocationList())
                {
                    var target = invocation.Target;
                    // UI and optional integrations may have replaced their subscriptions since capture.
                    // Preserve their current subscriptions instead of resurrecting retired external objects.
                    if (
                        target != null
                        && !map.ContainsKey(target)
                        && !_entries.ContainsKey(target)
                        && !live.AsValueEnumerable().Contains(invocation)
                    )
                        continue;
                    var rebound =
                        target != null && map.TryGetValue(target, out var newTarget)
                            ? Delegate.CreateDelegate(invocation.GetType(), newTarget, invocation.Method)
                            : invocation;
                    result = Delegate.Combine(result, rebound);
                }
                foreach (var invocation in live)
                    if (
                        (invocation.Target == null || !_owns(invocation.Target))
                        && !(result?.GetInvocationList().AsValueEnumerable().Contains(invocation) ?? false)
                    )
                        result = Delegate.Combine(result, invocation);
                return result;
            }
            return value;
        }
        // JSON containers implement IList, but JProperty cannot be cleared. Detach
        // their current children first, then rebuild using the original token objects
        // so native references and JSON parent/sibling links remain consistent.
        foreach (var entry in _entries.Values)
        {
            if (!entry.JsonToken)
                continue;
            var target = (JToken)Resolve(entry.Target)!;
            if (target is JProperty property)
                property.Value = JValue.CreateNull();
            else if (target is JContainer container)
                container.RemoveAll();
            else if (target is JValue scalar)
                scalar.Value = Resolve(entry.JsonValue);
        }
        foreach (var entry in _entries.Values)
        {
            if (!entry.JsonToken || entry.Items == null)
                continue;
            var target = (JContainer)Resolve(entry.Target)!;
            foreach (var saved in entry.Items)
            {
                var child = (JToken)Resolve(saved)!;
                if (child.Parent is JProperty parent)
                    parent.Value = JValue.CreateNull();
                else if (child.Parent != null)
                    child.Remove();
                if (target is JProperty property)
                    property.Value = child;
                else
                    target.Add(child);
            }
        }
        // Reconnect fields before collection population. Native code remains frozen until all roots are restored.
        foreach (var entry in _entries.Values)
        {
            var target = Resolve(entry.Target)!;
            foreach (var pair in entry.Fields)
            {
                pair.Field.SetValue(target, Resolve(pair.Value, pair.Field.GetValue(target)));
            }
        }
        foreach (var entry in _entries.Values)
        {
            if (entry.JsonToken)
                continue;
            var target = Resolve(entry.Target)!;
            if (entry.Pairs != null)
            {
                var dictionary = (IDictionary)target;
                dictionary.Clear();
                foreach (var pair in entry.Pairs)
                    dictionary.Add(Resolve(pair.Key)!, Resolve(pair.Value));
            }
            else if (entry.Items != null)
            {
                if (target is Array array)
                {
                    for (var index = 0; index < entry.Items.Length; index++)
                        array.SetValue(Resolve(entry.Items[index]), index);
                }
                else if (target is IList list)
                {
                    if (list.IsFixedSize)
                    {
                        if (list.Count != entry.Items.Length)
                            throw new InvalidOperationException("Native fixed checkpoint collection changed size.");
                        for (var index = 0; index < entry.Items.Length; index++)
                            list[index] = Resolve(entry.Items[index]);
                    }
                    else
                    {
                        list.Clear();
                        foreach (var item in entry.Items)
                            list.Add(Resolve(item));
                    }
                }
                else
                {
                    entry.SetClear!.Invoke(target, null);
                    foreach (var item in entry.Items)
                        entry.SetAdd!.Invoke(target, new[] { Resolve(item) });
                }
            }
        }
    }

    private void Capture(object? value, bool root = false)
    {
        if (value == null || value is string or Type or MemberInfo)
            return;
        var type = value.GetType();
        if (type.IsValueType)
        {
            if (!Scalar(type))
                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    Capture(field.GetValue(value));
            return;
        }
        if (value is Delegate callback)
        {
            foreach (var invocation in callback.GetInvocationList())
                if (invocation.Target?.GetType().IsDefined(typeof(CompilerGeneratedAttribute), false) == true)
                    Capture(invocation.Target);
            return;
        }
        if (_entries.ContainsKey(value) || (!root && !_owns(value)))
            return;
        if (_entries.Count >= 200000)
            throw new InvalidOperationException("Native checkpoint state exceeded its capture limit.");
        var entry = new Entry { Target = value };
        _entries.Add(value, entry);
        if (value is JToken token)
        {
            entry.JsonToken = true;
            if (token is JContainer container)
            {
                entry.Items = container.Children().AsValueEnumerable().Cast<object?>().ToArray();
                foreach (var child in entry.Items)
                    Capture(child, true);
            }
            else if (token is JValue scalar)
            {
                entry.JsonValue = scalar.Value;
                Capture(scalar.Value, true);
            }
            else
                throw new InvalidOperationException("Unsupported checkpoint JSON token: " + type.Name);
            return;
        }
        if (value is IDictionary dictionary && !dictionary.IsReadOnly && !dictionary.IsFixedSize)
        {
            entry.Pairs = new();
            foreach (DictionaryEntry pair in dictionary)
            {
                entry.Pairs.Add((pair.Key, pair.Value));
                Capture(pair.Key);
                Capture(pair.Value);
            }
            return;
        }
        if (value is IList list && (!list.IsReadOnly || value is Array))
        {
            if (value is Array array && array.Rank != 1)
                throw new InvalidOperationException("Unsupported native checkpoint array rank.");
            entry.Items = new object?[list.Count];
            list.CopyTo(entry.Items, 0);
            foreach (var item in entry.Items)
                Capture(item);
            return;
        }
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(HashSet<>))
        {
            entry.Items = ((IEnumerable)value).AsValueEnumerable().Cast<object?>().ToArray();
            entry.SetClear = type.GetMethod("Clear", Type.EmptyTypes);
            entry.SetAdd = type.GetMethod("Add", type.GenericTypeArguments);
            foreach (var item in entry.Items)
                Capture(item);
            return;
        }
        for (var current = type; current != null && current != typeof(object); current = current.BaseType)
            foreach (
                var field in current.GetFields(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly
                )
            )
            {
                if (_member != null && !_member(value, field))
                    continue;
                var saved = field.GetValue(value);
                entry.Fields.Add((field, saved));
                Capture(saved);
            }
    }

    private static bool Scalar(Type type) =>
        type.IsPrimitive
        || type.IsEnum
        || type == typeof(decimal)
        || type == typeof(DateTime)
        || type == typeof(DateTimeOffset)
        || type == typeof(TimeSpan)
        || type == typeof(Guid);
}
