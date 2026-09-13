using System.Reflection;
using Newtonsoft.Json.Linq;

namespace WTT.Campaigns.Tests;

internal static class ItemTemplateFingerprintChecks
{
    internal static void Run(Assembly client, Action<bool, string> check)
    {
        var capture = client
            .GetType("WTT.Campaigns.Client.Authoring.ItemTemplateFingerprint", true)!
            .GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic)!;
        var templateType = capture.GetParameters()[0].ParameterType.GetGenericArguments()[0];
        var native = templateType.Assembly;
        Type Native(string name) => native.GetType("EFT.InventoryLogic." + name, true)!;
        var mongoType = native.GetType("EFT.MongoID", true)!;
        object Id(string id) => Activator.CreateInstance(mongoType, id)!;
        Array ArrayOf(Type type, params object[] values)
        {
            var array = Array.CreateInstance(type, values.Length);
            for (var i = 0; i < values.Length; i++)
                array.SetValue(values[i], i);
            return array;
        }
        object Template(string type, string id)
        {
            var value = Activator.CreateInstance(Native(type))!;
            templateType.GetProperty("_id")!.SetValue(value, Id(id));
            return value;
        }
        const string weaponId = "111111111111111111111111",
            magazineId = "222222222222222222222222",
            filterId = "333333333333333333333333";
        var weapon = Template("WeaponTemplate", weaponId);
        var magazine = Template("MagazineTemplate", magazineId);
        var filterType = Native("ItemFilter");
        var filter = Activator.CreateInstance(filterType)!;
        filterType.GetField("Filter")!.SetValue(filter, ArrayOf(mongoType, Id(filterId)));
        var filters = ArrayOf(filterType, filter);
        var slotType = Native("Slot");
        var slot = Activator.CreateInstance(slotType, "mod_scope", filters, true, Enum.ToObject(Native("EParentMergeType"), 0))!;
        weapon.GetType().GetField("Slots")!.SetValue(weapon, ArrayOf(slotType, slot));
        var gridType = Native("Grid");
        var grid = Activator.CreateInstance(gridType, "main", 2, 3, false, false, filters, null, -1)!;
        weapon.GetType().GetField("Grids")!.SetValue(weapon, ArrayOf(gridType, grid));
        var stackType = Native("StackSlot");
        magazine
            .GetType()
            .GetField("Cartridges")!
            .SetValue(magazine, ArrayOf(stackType, Activator.CreateInstance(stackType, "cartridges", 30, filters)!));
        string Snapshot(params object[] values) => (string)capture.Invoke(null, [ArrayOf(templateType, values)])!;
        var baseline = Snapshot(weapon, magazine);
        var parsed = JObject.Parse(baseline);
        check(
            parsed[weaponId]!["type"]!.Value<string>() == "EFT.InventoryLogic.WeaponTemplate",
            "Actual native weapon templates fingerprint without the reverse type registry"
        );
        check(
            parsed[weaponId]!["fields"]!["Slots"]![0]!["filters"]![0]!["Filter"]![0]!.Value<string>() == filterId,
            "Fingerprint includes native slot filters and MongoIDs"
        );
        check(parsed[weaponId]!["fields"]!["Grids"]![0]!["height"]!.Value<int>() == 3, "Fingerprint preserves fixed grid height");
        check(
            parsed[magazineId]!["fields"]!["Cartridges"]![0]!["capacity"]!.Value<int>() == 30,
            "Fingerprint includes native ammunition capacity"
        );
        check(
            parsed[weaponId]!["fields"]!["DefAmmoTemplate"] == null && parsed[weaponId]!["fields"]!["_defAmmoTemplate"] == null,
            "Fingerprint never evaluates runtime getters or includes caches"
        );
        check(Snapshot(magazine, weapon) == baseline, "Template dictionary enumeration order does not change fingerprints");
        weapon.GetType().GetField("_defAmmoTemplate")!.SetValue(weapon, Activator.CreateInstance(Native("AmmoTemplate")));
        check(Snapshot(weapon, magazine) == baseline, "Populating runtime template caches leaves fingerprints unchanged");
        weapon.GetType().GetField("Width")!.SetValue(weapon, 5);
        check(Snapshot(weapon, magazine) != baseline, "Native template dimension changes invalidate fingerprints");
        weapon.GetType().GetField("Width")!.SetValue(weapon, 0);
        filterType.GetField("Filter")!.SetValue(filter, ArrayOf(mongoType, Id("444444444444444444444444")));
        check(Snapshot(weapon, magazine) != baseline, "Native compatibility filter changes invalidate fingerprints");
        Console.WriteLine("Item preview fingerprint: actual native weapon, grid, slot and ammunition data passed offline.");
    }
}
