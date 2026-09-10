using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace WTT.Campaigns.Shared.Serialization;

/// <summary>Typed imported contracts preserve their original wire shape, including absent fields and numeric strings.</summary>
[JsonConverter(typeof(NativeModelConverter))]
public abstract class NativeModel : ExtensibleJsonModel
{
    internal JObject? Source;
    internal JObject? Baseline;
}

// JSON containers are confined to this serialization boundary. They are never exposed to gameplay or editor code.
public sealed class NativeModelConverter : JsonConverter
{
    public override bool CanConvert(Type objectType)
    {
        return typeof(NativeModel).IsAssignableFrom(objectType);
    }

    public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null)
        {
            return null;
        }

        var source = JObject.Load(reader);
        var model = (NativeModel)Activator.CreateInstance(objectType)!;
        using (var input = source.CreateReader())
        {
            serializer.Populate(input, model);
        }

        model.Baseline = Fields(model, serializer);
        model.Source = source;
        return model;
    }

    private static JObject Fields(NativeModel model, JsonSerializer serializer)
    {
        var result = new JObject();
        var contract = (JsonObjectContract)serializer.ContractResolver.ResolveContract(model.GetType());
        foreach (var property in contract.Properties.Where(p => !p.Ignored && p.Readable))
        {
            var value = property.ValueProvider!.GetValue(model);
            if (value != null)
            {
                result[property.PropertyName!] = JToken.FromObject(value, serializer);
            }
        }
        model.WriteExtra(result);
        return result;
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        if (value == null)
        {
            writer.WriteNull();
            return;
        }
        var model = (NativeModel)value;
        var fields = Fields(model, serializer);
        var contract = (JsonObjectContract)serializer.ContractResolver.ResolveContract(model.GetType());
        if (model.Source != null && model.Baseline != null)
        {
            foreach (var key in fields.Properties().Select(p => p.Name).Union(model.Source.Properties().Select(p => p.Name)).ToArray())
            {
                if (contract.Properties.GetClosestMatchProperty(key)?.Ignored == true)
                {
                    continue;
                }

                if (!JToken.DeepEquals(fields[key], model.Baseline[key]))
                {
                    continue;
                }

                if (model.Source.TryGetValue(key, out var original))
                {
                    fields[key] = original.DeepClone();
                }
                else
                {
                    fields.Remove(key);
                }
            }
        }
        fields.WriteTo(writer);
    }
}
