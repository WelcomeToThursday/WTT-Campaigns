using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Locales;
using SPTarkov.Server.Core.Utils;
using WTT.Campaigns.Server.Progression;
using WTT.Campaigns.Server.Seasons;
using WTT.Campaigns.Shared.Story;
using Path = System.IO.Path;

namespace WTT.Campaigns.Server.Hub;

[Injectable(InjectionType.Singleton)]
public sealed class HubQuestService(TemplateTable templates, JsonUtil json, SeasonRepository repository, QuestBackportService backports)
{
    private Dictionary<string, NativeQuest> _captured = new();
    private readonly Dictionary<string, string> _unavailable = new();
    public HashSet<string> Imported { get; } = new();
    private readonly Dictionary<string, HashSet<string>> _seasonQuests = new();
    private readonly HashSet<string> _storyQuests = new();

    public bool Allowed(string questId, string seasonId)
    {
        return backports.Allowed(questId, seasonId)
            && (!Imported.Contains(questId) || _seasonQuests.TryGetValue(seasonId, out var quests) && quests.Contains(questId));
    }

    public void Initialize()
    {
        foreach (var runtime in repository.Playable.Values)
        {
            var definition = runtime.Definition;
            _storyQuests.UnionWith(definition.Story?.Quests.Select(q => q.QuestId) ?? []);
            _seasonQuests[definition.Id] = definition
                .Quests.Where(q => (bool?)q.SeasonalEnabled != false)
                .Select(q => (string)q.Id!)
                .ToHashSet();
        }
        _captured = repository
            .Playable.Values.SelectMany(r => r.Definition.Quests)
            .Where(q => (bool?)q.SeasonalEnabled != false)
            .GroupBy(q => (string)q.Id!)
            .ToDictionary(g => g.Key, g => g.First());
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

            var definition = _storyQuests.Contains(pair.Key)
                ? StoryQuestCompatibility.NativeTemplate(pair.Value)
                : WTT.Campaigns.Shared.Seasons.SeasonCompiler.Copy(pair.Value);
            definition.Localization = new();
            var native = Newtonsoft.Json.Linq.JObject.FromObject(definition);
            QuestBackportCompatibility.Normalize(native);
            templates.Quests[id] = json.Deserialize<Quest>(native.ToString())!;
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

        if (!_storyQuests.Contains(id) && backports.SkippedReason(id) is { } skipped)
        {
            return Fail(skipped);
        }

        if (!visiting.Add(id))
        {
            return Fail("The quest has a cyclic dependency requiring a compatibility adapter.");
        }

        if (QuestBackportCompatibility.Blocker(Newtonsoft.Json.Linq.JObject.FromObject(quest)) is { } stageBlocker)
        {
            return Fail(stageBlocker);
        }

        foreach (var condition in quest.AllConditions())
        {
            var kind = (string)condition.ConditionType!;
            // New story state, map triggers, and encounters cannot be inferred from objective text.
            if (
                _storyQuests.Contains(id)
                    ? !StoryQuestCompatibility.ConditionTypes.Contains(kind)
                    : kind is not ("Quest" or "Level" or "TraderLoyalty" or "FindItem" or "HandoverItem")
            )
            {
                return Fail("This quest requires a raid or story condition not yet supported by the installed SPT version: " + kind + ".");
            }

            if (kind == "Quest")
            {
                var targets = condition.Target ?? new StringTargets(Array.Empty<string>());
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
                foreach (var target in condition.Target!)
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
        foreach (var item in quest.AllItems())
        {
            if (!templates.Items.ContainsKey(new MongoId((string)item.Template!)))
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
