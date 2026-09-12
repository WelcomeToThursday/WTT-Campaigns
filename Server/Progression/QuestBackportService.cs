using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Routers;
using SPTarkov.Server.Core.Services.Locales;
using SPTarkov.Server.Core.Utils;
using Path = System.IO.Path;

namespace WTT.Campaigns.Server.Progression;

// Campaign items are registered at +500; progression consumes these quests at +700.
[Injectable(InjectionType.Singleton, OnLoadOrder.PostLoad + 650)]
public sealed class QuestBackportService(
    TemplateTable templates,
    JsonUtil json,
    LocaleService locales,
    LocaleTable localeTable,
    ImageRouter images
) : IOnLoad
{
    private readonly Dictionary<string, string?> _owners = new();
    private readonly Dictionary<string, string> _skipped = new();
    private bool _initialized;

    public bool Allowed(string questId, string campaignId)
    {
        return !_owners.TryGetValue(questId, out var owner) || owner == null || owner == campaignId;
    }

    public string? SkippedReason(string questId)
    {
        return _skipped.GetValueOrDefault(questId);
    }

    internal static JObject ReadManifest(string path)
    {
        var manifest = JObject.Parse(File.ReadAllText(path));
        if ((int?)manifest["Version"] != 1)
        {
            throw new InvalidDataException("Unsupported quest backport manifest version.");
        }
        var ids = new HashSet<string>();
        foreach (var entry in (JArray)manifest["Quests"]!)
        {
            var id = (string)entry["Quest"]!["_id"]!;
            if (!ids.Add(id) || (string?)manifest["Audit"]?[id]?["Status"] != "accepted" || entry["Quest"]!["status"] != null)
            {
                throw new InvalidDataException("Invalid or duplicated quest backport: " + id);
            }
        }
        return manifest;
    }

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return Task.CompletedTask;
        }
        var root = Metadata.DirectoryPath;
        var manifest = ReadManifest(Path.Combine(root, "data", "quest-backports.json"));
        foreach (var audit in ((JObject)manifest["Audit"]!).Properties())
        {
            if ((string?)audit.Value["Status"] == "skipped")
            {
                _skipped[audit.Name] = string.Join("; ", audit.Value["Blockers"]!.Select(b => (string?)b["Detail"] ?? (string?)b["Code"]));
            }
        }
        foreach (var entry in (JArray)manifest["Quests"]!)
        {
            var definition = (JObject)entry["Quest"]!.DeepClone();
            var id = (string)definition["_id"]!;
            if (templates.Quests.ContainsKey(new MongoId(id)))
            {
                continue; // Native or another installed mod already owns this definition.
            }
            var relativeIcon = (string)entry["Icon"]!;
            var iconRoot = Path.GetFullPath(Path.Combine(root, "data", "quest-icons")) + Path.DirectorySeparatorChar;
            var icon = Path.GetFullPath(Path.Combine(root, relativeIcon));
            if (
                !icon.StartsWith(iconRoot, StringComparison.OrdinalIgnoreCase)
                || !File.Exists(icon)
                || !string.Equals(
                    Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(icon))),
                    (string?)entry["IconSha256"],
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                throw new InvalidDataException("Quest backport icon is missing or changed: " + id);
            }
            var localization = (JObject)definition["localization"]!;
            definition.Remove("localization");
            QuestBackportCompatibility.Normalize(definition);
            var quest = json.Deserialize<Quest>(definition.ToString())!;
            // Recheck item requirements after installed content mods have loaded.
            foreach (var tpl in definition["rewards"]!.SelectTokens("$.._tpl").Select(t => (string)t!))
            {
                if (!templates.Items.ContainsKey(new MongoId(tpl)))
                {
                    throw new InvalidDataException("Quest backport reward item is no longer installed: " + tpl);
                }
            }
            foreach (var language in localeTable.Global.Keys)
            {
                var text = (JObject)localization["en"]!.DeepClone();
                if (localization[language] is JObject translated)
                {
                    foreach (var pair in translated.Properties().Where(p => !string.IsNullOrWhiteSpace((string?)p.Value)))
                    {
                        text[pair.Name] = pair.Value.DeepClone();
                    }
                }
                foreach (var pair in text.Properties())
                {
                    locales.GetLocaleDb(language)[pair.Name] = (string)pair.Value!;
                }
            }
            images.AddRoute("/wtt-campaigns/quest-icons/" + id, icon);
            templates.Quests[new MongoId(id)] = quest;
            _owners[id] = (string?)entry["CampaignId"];
        }
        _initialized = true;
        return Task.CompletedTask;
    }
}
