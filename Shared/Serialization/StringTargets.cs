using System.Collections;
using Newtonsoft.Json;

namespace WTT.Campaigns.Shared.Serialization;

/// <summary>Native target fields accept one identifier or a list of identifiers.</summary>
[JsonConverter(typeof(StringTargetsConverter))]
public sealed class StringTargets : IEnumerable<string>
{
    public bool IsList { get; }
    public List<string> Values { get; }

    public StringTargets(string value)
    {
        Values = new() { value };
    }

    public StringTargets(IEnumerable<string> values)
    {
        IsList = true;
        Values = values.AsValueEnumerable().ToList();
    }

    public static implicit operator StringTargets(string value)
    {
        return new(value);
    }

    public static implicit operator StringTargets(string[] values)
    {
        return new(values);
    }

    public static explicit operator string?(StringTargets? value)
    {
        return value?.Values.AsValueEnumerable().FirstOrDefault();
    }

    public IEnumerator<string> GetEnumerator()
    {
        return Values.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public override string ToString()
    {
        return Values.AsValueEnumerable().FirstOrDefault() ?? "";
    }
}

public sealed class StringTargetsConverter : JsonConverter<StringTargets>
{
    public override StringTargets? ReadJson(
        JsonReader reader,
        Type objectType,
        StringTargets? existingValue,
        bool hasExistingValue,
        JsonSerializer serializer
    )
    {
        return reader.TokenType switch
        {
            JsonToken.Null => null,
            JsonToken.String => new StringTargets((string)reader.Value!),
            JsonToken.StartArray => new StringTargets(serializer.Deserialize<List<string>>(reader)!),
            _ => throw new JsonSerializationException("A target must be an identifier or a list of identifiers."),
        };
    }

    public override void WriteJson(JsonWriter writer, StringTargets? value, JsonSerializer serializer)
    {
        if (value == null)
        {
            writer.WriteNull();
        }
        else if (value.IsList)
        {
            serializer.Serialize(writer, value.Values);
        }
        else
        {
            writer.WriteValue(value.Values.AsValueEnumerable().Single());
        }
    }
}
