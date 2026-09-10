using System.Collections;
using System.Reflection;
using Newtonsoft.Json;

namespace WTT.Campaigns.Shared.Serialization;

/// <summary>Walks declared model properties for authoring references without converting models to JSON trees.</summary>
public static class ModelGraph
{
    public sealed class TextValue
    {
        public string Path { get; internal set; } = "";
        public string Field { get; internal set; } = "";
        public string Value { get; internal set; } = "";
        public IReadOnlyList<object> Ancestors { get; internal set; } = Array.Empty<object>();
        internal Action<string> Write = null!;

        public void Set(string value)
        {
            Write(value);
        }

        public bool IsIdentity
        {
            get { return Field is "Id" or "id" or "_id"; }
        }
    }

    public static string Name(PropertyInfo property)
    {
        return property.GetCustomAttribute<JsonPropertyAttribute>()?.PropertyName ?? property.Name;
    }

    public static IEnumerable<PropertyInfo> Properties(object value)
    {
        return value
            .GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p =>
                p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0 && p.GetCustomAttribute<JsonIgnoreAttribute>() == null
            );
    }

    public static string? Id(object value)
    {
        return value.GetType().GetProperty("Id")?.GetValue(value) as string;
    }

    public static IEnumerable<TextValue> Texts(object source)
    {
        return Visit(source, "", "", Array.Empty<object>(), _ => { });
    }

    private static IEnumerable<TextValue> Visit(object? value, string path, string field, IReadOnlyList<object> parents, Action<string> set)
    {
        if (value is string text)
        {
            yield return new TextValue
            {
                Path = path,
                Field = field,
                Value = text,
                Ancestors = parents,
                Write = set,
            };
            yield break;
        }
        if (value == null || value.GetType().IsValueType || parents.Any(p => ReferenceEquals(p, value)))
        {
            yield break;
        }

        var ancestors = parents.Concat(new[] { value }).ToArray();
        if (value is StringTargets targets)
        {
            for (var i = 0; i < targets.Values.Count; i++)
            {
                var index = i;
                foreach (
                    var leaf in Visit(
                        targets.Values[i],
                        path + (targets.IsList ? "[" + i + "]" : ""),
                        field,
                        ancestors,
                        v => targets.Values[index] = v
                    )
                )
                {
                    yield return leaf;
                }
            }
        }
        else if (value is IDictionary dictionary)
        {
            foreach (var key in dictionary.Keys.Cast<object>().ToArray())
            {
                foreach (
                    var leaf in Visit(
                        dictionary[key],
                        path + (path.Length == 0 ? "" : ".") + key,
                        key.ToString()!,
                        ancestors,
                        v => dictionary[key] = v
                    )
                )
                {
                    yield return leaf;
                }
            }
        }
        else if (value is IList list)
        {
            for (var i = 0; i < list.Count; i++)
            {
                var index = i;
                foreach (var leaf in Visit(list[i], path + "[" + i + "]", field, ancestors, v => list[index] = v))
                {
                    yield return leaf;
                }
            }
        }
        else if (value is IEnumerable sequence)
        {
            var i = 0;
            foreach (var entry in sequence)
            {
                foreach (
                    var leaf in Visit(
                        entry,
                        path + "[" + i++ + "]",
                        field,
                        ancestors,
                        _ => throw new InvalidOperationException("Cannot edit this collection.")
                    )
                )
                {
                    yield return leaf;
                }
            }
        }
        else
        {
            foreach (var property in Properties(value))
            {
                var name = Name(property);
                foreach (
                    var leaf in Visit(
                        property.GetValue(value),
                        path + (path.Length == 0 ? "" : ".") + name,
                        name,
                        ancestors,
                        v => property.SetValue(value, v)
                    )
                )
                {
                    yield return leaf;
                }
            }
        }
    }

    public static void Rewrite(object source, Func<string, string> replace)
    {
        var texts = Texts(source).ToArray();
        foreach (var text in texts)
        {
            text.Set(replace(text.Value));
        }

        foreach (var dictionary in texts.SelectMany(t => t.Ancestors).OfType<IDictionary>().Distinct())
        {
            foreach (var key in dictionary.Keys.OfType<string>().ToArray())
            {
                var next = replace(key);
                if (key == next)
                {
                    continue;
                }

                var value = dictionary[key];
                dictionary.Remove(key);
                dictionary.Add(next, value);
            }
        }
    }
}
