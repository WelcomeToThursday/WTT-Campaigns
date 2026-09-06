using Newtonsoft.Json.Linq;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Locales;
using SPTarkov.Server.Core.Utils;
using Path = System.IO.Path;

namespace SeasonalPerks.Server.Items;

[Injectable(InjectionType.Singleton, OnLoadOrder.Preload + 2000)]
public sealed class SeasonItemService(TemplateTable templates, LocaleService locales, JsonUtil json) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var items = JObject.Parse(File.ReadAllText(Path.Combine(Metadata.DirectoryPath, "data/season-items.json")));
        foreach (var pair in items.Properties())
        {
            var id = new MongoId(pair.Name);
            if (templates.Items.ContainsKey(id))
            {
                continue;
            }
            var item = (JObject)pair.Value.DeepClone();
            var bundle = "wtt-seasonal/" + (string)item["_props"]!["Prefab"]!["path"]!;
            if (!File.Exists(Path.Combine(Metadata.DirectoryPath, "bundles", bundle)))
            {
                continue;
            }
            item["_props"]!["Prefab"]!["path"] = bundle;
            if ((string?)item["_parent"] == "6a28212a0368f4438b0d0a45")
            {
                // EFT 0.16 has no BattlePassItem factory. Native Info items retain the original inventory behavior and dimensions.
                item["_parent"] = "5448ecbe4bdc2d60728b4568";
            }
            templates.Items[id] = json.Deserialize<TemplateItem>(item.ToString())!;
            if (!templates.Handbook.Items.Any(i => i.Id == id))
            {
                templates.Handbook.Items.Add(
                    new HandbookItem
                    {
                        Id = id,
                        ParentId = new MongoId("5b47574386f77428ca22b341"),
                        Price = 0,
                    }
                );
            }
        }
        var locale = json.DeserializeFromFile<Dictionary<string, string>>(
            Path.Combine(Metadata.DirectoryPath, "data/locales/season-items-en.json")
        )!;
        foreach (var pair in locale)
        {
            locales.GetLocaleDb("en")[pair.Key] = pair.Value;
        }
        return Task.CompletedTask;
    }
}
