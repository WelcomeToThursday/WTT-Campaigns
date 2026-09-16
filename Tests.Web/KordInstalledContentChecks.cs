using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Hideout;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Locales;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Json;
using WTT.Campaigns.Server.Seasons;
using WTT.Campaigns.Server.Web.Authoring;
using WTT.Campaigns.Shared.Seasons;
using Path = System.IO.Path;

internal static class KordInstalledContentChecks
{
    // File-only database contract check. No host, profile, application or mod startup is executed.
    internal static void Run(string game, string source)
    {
        var temporary = Path.Combine(Path.GetTempPath(), "kord-contract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            foreach (var folder in new[] { "data", "hub-images", "icons" })
            foreach (var file in Directory.EnumerateFiles(Path.Combine(source, folder), "*", SearchOption.AllDirectories))
            {
                var destination = Path.Combine(temporary, folder, Path.GetRelativePath(Path.Combine(source, folder), file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination);
            }
            var repository = (SeasonRepository)
                Activator.CreateInstance(
                    typeof(SeasonRepository),
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    null,
                    [temporary],
                    null
                )!;
            var definition = repository.Create(true, repository.Legacy).Definition;
            var json = new JsonUtil([new SptJsonConverterRegistrator()]);
            var database = Path.Combine(game, "SPT_Runtime", "SPT_Data", "database");
            var backport = Path.Combine(game, "SPT_Runtime", "user", "mods", "WTT-ContentBackport");
            var items = JObject.Parse(File.ReadAllText(Path.Combine(database, "templates", "items.json")));
            var pending = new Dictionary<string, JObject>();
            foreach (
                var file in Directory.EnumerateFiles(Path.Combine(backport, "db", "CustomItems"), "*.json", SearchOption.AllDirectories)
            )
            foreach (var entry in JObject.Parse(File.ReadAllText(file)).Properties())
                if (entry.Value is JObject value && value["itemTplToClone"] != null)
                    pending[entry.Name] = value;
            while (pending.Count > 0)
            {
                var ready = pending.Where(p => items[(string)p.Value["itemTplToClone"]!] != null).ToArray();
                if (ready.Length == 0)
                    throw new Exception("Unresolved backport template sources: " + string.Join(", ", pending.Keys));
                foreach (var pair in ready)
                {
                    var item = (JObject)items[(string)pair.Value["itemTplToClone"]!]!.DeepClone();
                    item["_id"] = pair.Key;
                    item["_parent"] = pair.Value["parentId"]?.DeepClone() ?? item["_parent"];
                    ((JObject)item["_props"]!).Merge(
                        pair.Value["overrideProperties"],
                        new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Replace }
                    );
                    items[pair.Key] = item;
                    pending.Remove(pair.Key);
                }
            }
            var table = JsonConvert.DeserializeObject<TemplateTable>("{}")!;
            void Set(string property, object value) => typeof(TemplateTable).GetProperty(property)!.SetValue(table, value);
            void Load(string property, string file)
            {
                var type = typeof(TemplateTable).GetProperty(property)!.PropertyType;
                var value = typeof(JsonUtil)
                    .GetMethods()
                    .Single(m => m.Name == "Deserialize" && m.IsGenericMethod && m.GetParameters().Length == 1)
                    .MakeGenericMethod(type)
                    .Invoke(json, [File.ReadAllText(Path.Combine(database, "templates", file))])!;
                Set(property, value);
            }
            Set(nameof(TemplateTable.Items), json.Deserialize<Dictionary<MongoId, TemplateItem>>(items.ToString())!);
            Set(
                nameof(TemplateTable.Quests),
                json.Deserialize<Dictionary<MongoId, Quest>>(File.ReadAllText(Path.Combine(database, "templates", "quests.json")))!
            );
            Load(nameof(TemplateTable.Profiles), "profiles.json");
            Load(nameof(TemplateTable.Customization), "customization.json");
            foreach (
                var file in Directory.EnumerateFiles(
                    Path.Combine(backport, "db", "CustomCustomization", "Customization"),
                    "*.json",
                    SearchOption.AllDirectories
                )
            )
            foreach (var pair in json.Deserialize<Dictionary<MongoId, CustomizationItem>>(File.ReadAllText(file))!)
                table.Customization[pair.Key] = pair.Value;
            foreach (
                var file in Directory.EnumerateFiles(Path.Combine(backport, "db", "CustomClothing"), "*.json", SearchOption.AllDirectories)
            )
            foreach (var clothing in JArray.Parse(File.ReadAllText(file)).OfType<JObject>())
            {
                var id = (string?)clothing["suiteId"];
                if (id == null)
                    continue;
                var upper = (string?)clothing["type"] == "top";
                table.Customization[new MongoId(id)] = json.Deserialize<CustomizationItem>(
                    new JObject
                    {
                        ["_id"] = id,
                        ["_name"] = id,
                        ["_type"] = "Item",
                        ["_parent"] = upper ? "5cd944ca1388ce03a44dc2a4" : "5cd944d01388ce000a659df9",
                        ["_props"] = new JObject(),
                    }.ToString()
                )!;
            }
            var customAssorts = new JObject();
            foreach (var kind in new[] { "CustomHeads", "CustomVoices" })
            foreach (var file in Directory.EnumerateFiles(Path.Combine(backport, "db", kind), "*.json", SearchOption.AllDirectories))
            foreach (var entry in JObject.Parse(File.ReadAllText(file)).Properties())
                table.Customization[new MongoId(entry.Name)] = json.Deserialize<CustomizationItem>(
                    new JObject
                    {
                        ["_id"] = entry.Name,
                        ["_name"] = entry.Name,
                        ["_type"] = "Item",
                        ["_parent"] = kind == "CustomHeads" ? CustomisationTypeId.HEAD : CustomisationTypeId.VOICE,
                        ["_props"] = new JObject(),
                    }.ToString()
                )!;
            foreach (
                var file in Directory.EnumerateFiles(
                    Path.Combine(backport, "db", "CustomAssortSchemes"),
                    "*.json",
                    SearchOption.AllDirectories
                )
            )
                customAssorts.Merge(JObject.Parse(File.ReadAllText(file)));
            var traders = new TradersTable();
            foreach (var directory in Directory.EnumerateDirectories(Path.Combine(database, "traders")))
                if (File.Exists(Path.Combine(directory, "base.json")))
                {
                    var native = new JObject
                    {
                        ["base"] = JObject.Parse(File.ReadAllText(Path.Combine(directory, "base.json"))),
                        ["dialogue"] = new JObject(),
                        ["questassort"] = new JObject(),
                        ["assort"] = new JObject
                        {
                            ["items"] = new JArray(),
                            ["barter_scheme"] = new JObject(),
                            ["loyal_level_items"] = new JObject(),
                        },
                    };
                    if (File.Exists(Path.Combine(directory, "assort.json")))
                        native["assort"] = JObject.Parse(File.ReadAllText(Path.Combine(directory, "assort.json")));
                    var name = ((string?)native["base"]?["nickname"])?.ToUpperInvariant() ?? "";
                    if (customAssorts[name] is JObject additions)
                        ((JObject)native["assort"]!).Merge(additions);
                    traders[new MongoId(Path.GetFileName(directory))] = json.Deserialize<Trader>(native.ToString())!;
                }
            var localeTable = new LocaleTable
            {
                Global = new() { ["en"] = new(() => new GlobalLocaleDictionary(), cacheValue: true) },
                Menu = [],
                Languages = [],
            };
            var locale = new LocaleService(null!, localeTable, null!);
            var catalogue = new TraderOfferCatalogue(
                table,
                traders,
                null!,
                locale,
                json,
                WTT.Campaigns.Tests.NativeItemHelperFixture.Create(table)
            );
            var previews = new ItemPreviewService(repository, catalogue);
            var hideout = JsonConvert.DeserializeObject<HideoutTable>("{}")!;
            typeof(HideoutTable)
                .GetProperty(nameof(HideoutTable.Production))!
                .SetValue(
                    hideout,
                    json.Deserialize<SPTarkov.Server.Core.Models.Eft.Hideout.HideoutProductionData>("{\"recipes\":[],\"scavRecipes\":[]}")!
                );
            var mods = new[] { ("BlackDivServer", "com.blackdiv.tacticaltoaster"), ("WTT-ContentBackport", "com.wtt.contentbackport") }
                .Select(m => new SptMod
                {
                    Directory = Path.Combine(game, "SPT_Runtime", "user", "mods", m.Item1),
                    ModMetadata = new WTT.Campaigns.Server.Metadata { ModGuid = m.Item2 },
                    Assemblies = [],
                })
                .ToArray();
            if (mods.Any(m => !Directory.Exists(m.Directory)))
                throw new Exception("Required installed KORD mod directory is absent.");
            var content = new SeasonContentService(
                repository,
                table,
                traders,
                null!,
                locale,
                localeTable,
                hideout,
                json,
                null!,
                mods,
                catalogue,
                previews
            );
            var validation = content.Validate(definition);
            if (!validation.CanActivate)
                throw new Exception(JsonConvert.SerializeObject(validation.Issues, Formatting.Indented));
            foreach (var rule in definition.QuestLoot)
                if (table.Items.GetValueOrDefault(new MongoId(rule.ItemTemplate))?.Properties?.QuestItem == true)
                    throw new Exception("Bot loot must be a native inventory item.");
            Console.WriteLine(
                "PASS installed KORD content: 12 active quests, native reward assemblies, bot loot templates, owned recipe and source assets."
            );
        }
        finally
        {
            Directory.Delete(temporary, true);
        }
    }
}
