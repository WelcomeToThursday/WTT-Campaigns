using Newtonsoft.Json.Linq;
using SeasonalPerks.Server.Seasons;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Locales;
using SPTarkov.Server.Core.Utils;
using Path = System.IO.Path;

namespace SeasonalPerks.Server.Hub;

[Injectable(InjectionType.Singleton)]
public sealed class HubQuestService(TemplateTable templates, JsonUtil json, SeasonRepository repository)
{
    private Dictionary<string, JObject> _captured = new();
    private readonly Dictionary<string, string> _unavailable = new();
    public HashSet<string> Imported { get; } = new();

    public void Initialize()
    {
        _captured = repository
            .Current.Definition.Quests.OfType<JObject>()
            .Where(q => (bool?)q["_seasonalEnabled"] != false)
            .ToDictionary(q => (string)q["_id"]!);
        foreach (var id in _captured.Keys)
        {
            Validate(id, new HashSet<string>());
        }

        foreach (var pair in _captured.Where(p => !_unavailable.ContainsKey(p.Key)))
        {
            var id = new MongoId(pair.Key);
            if (templates.Quests.ContainsKey(id))
            {
                continue;
            }

            var definition = (JObject)pair.Value.DeepClone();
            definition.Remove("localization");
            templates.Quests[id] = json.Deserialize<Quest>(definition.ToString())!;
            Imported.Add(pair.Key);
        }
    }

    public string UnavailableReason(string id)
    {
        if (id == "6a4f82e11b7350af050b2e1c" && !templates.Quests.ContainsKey(new MongoId(id)))
        {
            return "Historical Perspectives is unavailable until its full quest definition is recovered.";
        }
        return _unavailable.GetValueOrDefault(id)
            ?? (templates.Quests.ContainsKey(new MongoId(id)) ? "" : "The required quest definition has not been recovered.");
    }

    private bool Validate(string id, HashSet<string> visiting)
    {
        if (templates.Quests.ContainsKey(new MongoId(id)))
        {
            return true;
        }

        if (_unavailable.ContainsKey(id))
        {
            return false;
        }

        if (!_captured.TryGetValue(id, out var quest))
        {
            return Fail("The required quest definition has not been recovered.");
        }

        if (!visiting.Add(id))
        {
            return Fail("The quest has a cyclic dependency requiring a compatibility adapter.");
        }

        foreach (
            var condition in ((JContainer)quest["conditions"]!)
                .DescendantsAndSelf()
                .OfType<JObject>()
                .Where(c => c["conditionType"] != null)
        )
        {
            var kind = (string)condition["conditionType"]!;
            // New story state, map triggers, and encounters cannot be inferred from objective text.
            if (kind is not ("Quest" or "Level" or "TraderLoyalty" or "FindItem" or "HandoverItem"))
            {
                return Fail("This quest requires a raid or story condition not yet supported by the installed SPT version: " + kind + ".");
            }

            if (kind == "Quest")
            {
                var targets = condition["target"] is JArray array ? array.Select(v => (string)v!) : new[] { (string)condition["target"]! };
                foreach (var target in targets)
                {
                    if (!Validate(target, new HashSet<string>(visiting)))
                    {
                        return Fail(
                            "A prerequisite is unavailable: " + UnavailableReason(target).Replace("A prerequisite is unavailable: ", "")
                        );
                    }
                }
            }
            if (kind is "FindItem" or "HandoverItem")
            {
                foreach (var target in condition["target"]!)
                {
                    if (!templates.Items.TryGetValue(new MongoId((string)target!), out var item))
                    {
                        return Fail("A required quest item is not installed.");
                    }

                    if (item.Properties?.QuestItem == true)
                    {
                        return Fail("The original quest-item placement has not been verified for SPT.");
                    }
                }
            }
        }
        foreach (var item in ((JContainer)quest["rewards"]!).DescendantsAndSelf().OfType<JObject>().Where(n => n["_tpl"] != null))
        {
            if (!templates.Items.ContainsKey(new MongoId((string)item["_tpl"]!)))
            {
                return Fail("A quest reward item is not installed.");
            }
        }

        return true;
        bool Fail(string reason)
        {
            _unavailable[id] = reason;
            return false;
        }
    }
}
