using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Locales;
using SPTarkov.Server.Core.Utils;
using WTT.Campaigns.Server.Progression;
using WTT.Campaigns.Server.Seasons;
using WTT.Campaigns.Shared.Seasons;
using WTT.Campaigns.Shared.Story;
using Path = System.IO.Path;

namespace WTT.Campaigns.Server.Hub;

[Injectable(InjectionType.Singleton)]
public sealed class HubQuestService(
    TemplateTable templates,
    JsonUtil json,
    SeasonRepository repository,
    QuestBackportService backports,
    LocaleService locales,
    LocaleTable localeTable
)
{
    private Dictionary<string, NativeQuest> _captured = new();
    private readonly Dictionary<string, string> _unavailable = new();
    public HashSet<string> Imported { get; } = new();
    private readonly Dictionary<string, HashSet<string>> _seasonQuests = new();
    private readonly HashSet<string> _storyQuests = new();
    private readonly Dictionary<string, string> _isolatedQuestSeasons = new(StringComparer.Ordinal);

    public bool Allowed(string questId, string seasonId)
    {
        // Disposable campaign copies own their quest ids for the lifetime of
        // the test. They must never become visible to another campaign merely
        // because the native template table contains the copied id.
        if (_isolatedQuestSeasons.TryGetValue(questId, out var isolatedSeason))
            return isolatedSeason == seasonId;

        return backports.Allowed(questId, seasonId)
            && (!Imported.Contains(questId) || _seasonQuests.TryGetValue(seasonId, out var quests) && quests.Contains(questId));
    }

    /// <summary>
    /// Installs native quest templates and locale keys for one in-memory
    /// campaign copy. The returned registration restores every replaced native
    /// entry and removes the campaign ownership scope.
    /// </summary>
    public IDisposable RegisterIsolated(SeasonDefinition definition)
    {
        if (definition == null || string.IsNullOrWhiteSpace(definition.Id))
            throw new InvalidDataException("An isolated campaign requires a valid identity.");

        var quests = definition.Quests.Where(q => (bool?)q.SeasonalEnabled != false).ToArray();
        var installed = new List<string>();
        var storyIds = definition.Story?.Quests.Select(q => q.QuestId).Distinct(StringComparer.Ordinal).ToArray() ?? [];
        var previousLocales = new Dictionary<(string Language, string Key), (bool Present, string? Value)>();
        try
        {
            foreach (var source in quests)
            {
                var id = (string?)source.Id;
                if (string.IsNullOrWhiteSpace(id) || !MongoId.IsValidMongoId(id))
                    throw new InvalidDataException("An isolated campaign contains an invalid quest identity.");
                if (_isolatedQuestSeasons.ContainsKey(id) || templates.Quests.ContainsKey(new MongoId(id)))
                    throw new InvalidOperationException("The isolated campaign quest identity is already registered: " + id);

                var storyQuest = definition.Story?.Quests.Any(q => q.QuestId == id) == true;
                var nativeSource = storyQuest ? StoryQuestCompatibility.NativeTemplate(source) : SeasonCompiler.Copy(source);
                var localized = nativeSource.Localization;
                nativeSource.Localization = new();
                var nativeJson = Newtonsoft.Json.Linq.JObject.FromObject(nativeSource);
                QuestBackportCompatibility.Normalize(nativeJson);
                templates.Quests[new MongoId(id)] = json.Deserialize<Quest>(nativeJson.ToString())!;
                installed.Add(id);
                _isolatedQuestSeasons[id] = definition.Id;

                foreach (var language in localeTable.Global.Keys)
                {
                    var fallback = localized.GetValueOrDefault("en") ?? new();
                    var translated = localized.GetValueOrDefault(language);
                    foreach (var pair in fallback)
                    {
                        var value = translated?.GetValueOrDefault(pair.Key);
                        if (string.IsNullOrWhiteSpace(value))
                            value = pair.Value;
                        previousLocales.TryAdd(
                            (language, pair.Key),
                            locales.GetLocaleDb(language).TryGetValue(pair.Key, out var old) ? (true, old) : (false, null)
                        );
                        locales.GetLocaleDb(language)[pair.Key] = value;
                    }
                    if (translated != null)
                    {
                        foreach (var pair in translated.Where(p => !fallback.ContainsKey(p.Key)))
                        {
                            previousLocales.TryAdd(
                                (language, pair.Key),
                                locales.GetLocaleDb(language).TryGetValue(pair.Key, out var old) ? (true, old) : (false, null)
                            );
                            locales.GetLocaleDb(language)[pair.Key] = pair.Value;
                        }
                    }
                }
            }

            _seasonQuests[definition.Id] = quests.Select(q => (string)q.Id!).ToHashSet(StringComparer.Ordinal);
            _storyQuests.UnionWith(storyIds);

            return new Registration(this, definition.Id, installed, storyIds, previousLocales);
        }
        catch
        {
            RemoveIsolated(definition.Id, installed, storyIds, previousLocales);
            throw;
        }
    }

    private sealed class Registration(
        HubQuestService owner,
        string seasonId,
        IReadOnlyCollection<string> quests,
        IReadOnlyCollection<string> storyIds,
        IReadOnlyDictionary<(string Language, string Key), (bool Present, string? Value)> locales
    ) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                owner.RemoveIsolated(seasonId, quests, storyIds, locales);
        }
    }

    private void RemoveIsolated(
        string seasonId,
        IEnumerable<string> quests,
        IEnumerable<string> storyIds,
        IReadOnlyDictionary<(string Language, string Key), (bool Present, string? Value)> previousLocales
    )
    {
        foreach (var id in quests)
        {
            _isolatedQuestSeasons.Remove(id);
            templates.Quests.Remove(new MongoId(id));
        }
        _seasonQuests.Remove(seasonId);
        foreach (var id in storyIds)
        {
            if (
                !_seasonQuests.Values.Any(q => q.Contains(id))
                && !repository.OrdinaryPlayable().Any(r => r.Definition.Story?.Quests.Any(s => s.QuestId == id) == true)
            )
                _storyQuests.Remove(id);
        }
        foreach (var id in _isolatedQuestSeasons.Where(pair => pair.Value == seasonId).Select(pair => pair.Key).ToArray())
            _isolatedQuestSeasons.Remove(id);
        foreach (var language in previousLocales.Keys.Select(k => k.Language).Distinct(StringComparer.Ordinal))
        {
            var db = locales.GetLocaleDb(language);
            foreach (var key in previousLocales.Keys.Where(k => k.Language == language))
            {
                var old = previousLocales[key];
                if (old.Present)
                    db[key.Key] = old.Value!;
                else
                    db.Remove(key.Key);
            }
        }
    }

    public void Initialize()
    {
        foreach (var runtime in repository.OrdinaryPlayable())
        {
            var definition = runtime.Definition;
            _storyQuests.UnionWith(definition.Story?.Quests.Select(q => q.QuestId) ?? []);
            _seasonQuests[definition.Id] = definition
                .Quests.Where(q => (bool?)q.SeasonalEnabled != false)
                .Select(q => (string)q.Id!)
                .ToHashSet();
        }
        _captured = repository
            .OrdinaryPlayable()
            .SelectMany(r => r.Definition.Quests)
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
