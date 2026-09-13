using System.Reflection;
using EFT;
using EFT.InventoryLogic;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using ZLinq;

namespace WTT.Campaigns.Client.Authoring;

internal static class ItemTemplateFingerprint
{
    // EFT's template converter is a network reader, not a template snapshot writer:
    // its reverse type registry is incomplete and its slot writers omit filters.
    // Capture data fields without invoking template getters or runtime caches.
    private static readonly FieldContracts Contracts = new();

    internal static string Capture(IEnumerable<ItemTemplate> templates)
    {
        var snapshot = new SortedDictionary<string, object>(StringComparer.Ordinal);
        foreach (var template in templates)
        {
            snapshot.Add(
                template.StringId,
                new SortedDictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["parent"] = template.ParentId?.ToString(),
                    ["type"] = template.GetType().FullName,
                    ["node"] = template._type,
                    ["fields"] = template,
                }
            );
        }
        // Create (not CreateDefault) prevents global game/mod serializer settings
        // from reintroducing the network converters.
        var serializer = JsonSerializer.Create(
            new JsonSerializerSettings
            {
                ContractResolver = Contracts,
                Converters = [new ContainerData()],
                Culture = System.Globalization.CultureInfo.InvariantCulture,
            }
        );
        using var output = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        using var writer = new JsonTextWriter(output);
        serializer.Serialize(writer, snapshot);
        return output.ToString();
    }

    private sealed class FieldContracts : DefaultContractResolver
    {
        protected override JsonConverter? ResolveContractConverter(Type objectType) => null;

        protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization memberSerialization)
        {
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
            Array.Sort(fields, (a, b) => StringComparer.Ordinal.Compare(a.Name, b.Name));
            var properties = new List<JsonProperty>();
            foreach (var field in fields)
            {
                if (field.IsDefined(typeof(NonSerializedAttribute)) || field.IsDefined(typeof(JsonIgnoreAttribute)))
                    continue;
                var property = CreateProperty(field, MemberSerialization.Fields);
                property.Converter = null;
                property.ItemConverter = null;
                properties.Add(property);
            }
            return properties;
        }
    }

    private sealed class ContainerData : JsonConverter
    {
        public override bool CanRead => false;

        public override bool CanConvert(Type type) =>
            type == typeof(MongoID)
            || typeof(Slot).IsAssignableFrom(type)
            || typeof(Grid).IsAssignableFrom(type)
            || typeof(StackSlot).IsAssignableFrom(type);

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            if (value is MongoID id)
            {
                writer.WriteValue(id.ToString());
                return;
            }
            var data = new SortedDictionary<string, object?>(StringComparer.Ordinal) { ["type"] = value!.GetType().FullName };
            switch (value)
            {
                case Slot slot:
                    data["id"] = slot.ID;
                    data["filters"] = slot.Filters;
                    data["required"] = slot.Required;
                    data["merge"] = slot.MergeContainerWithChildren;
                    data["locked"] = slot.Locked;
                    data["armorColliders"] = slot.ArmorColliders;
                    data["armorPlate"] = slot.ArmorPlateColliderMask;
                    data["bluntDamage"] = slot.BluntDamageReduceFromSoftArmor;
                    data["blockers"] = slot
                        .BlockerSlots.AsValueEnumerable()
                        .Select(s => s.ID)
                        .OrderBy(s => s, StringComparer.Ordinal)
                        .ToArray();
                    data["conflicts"] =
                        slot.ConflictingSlots == null
                            ? null
                            : slot.ConflictingSlots.Keys.AsValueEnumerable().OrderBy(s => s, StringComparer.Ordinal).ToArray();
                    break;
                case Grid grid:
                    data["id"] = grid.ID;
                    data["width"] = grid.GridWidth;
                    data["height"] = grid.GridHeight;
                    data["capacity"] = grid.MaxItemsCount;
                    data["stretchHorizontal"] = grid.CanStretchHorizontally;
                    data["stretchVertical"] = grid.CanStretchVertically;
                    data["filters"] = grid.Filters;
                    break;
                case StackSlot stack:
                    data["id"] = stack.ID;
                    data["capacity"] = stack.MaxCount;
                    data["filters"] = stack.Filters;
                    break;
            }
            serializer.Serialize(writer, data);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer) =>
            throw new NotSupportedException();
    }
}
