using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils;
using Path = System.IO.Path;

namespace WTT.Campaigns.Server.Progression;

[Injectable(InjectionType.Singleton)]
public sealed class ReputationMigrationService(SaveServer saves, JsonUtil json)
{
    private List<ReputationRevision>? _revisions;

    public bool Apply(PmcData profile)
    {
        if (_revisions == null)
        {
            var report = JObject.Parse(File.ReadAllText(Path.Combine(Metadata.DirectoryPath, "data", "trader-progression-reachability.json")));
            if ((int?)report["Version"] != 1)
            {
                throw new InvalidDataException("Unsupported reputation adjustment report.");
            }
            _revisions = report["Adjustments"]!.ToObject<List<ReputationRevision>>()!
                .GroupBy(r => r.QuestId + "/" + r.RewardId)
                .Select(g => new ReputationRevision
                {
                    QuestId = g.First().QuestId,
                    RewardId = g.First().RewardId,
                    TraderId = g.First().TraderId,
                    Before = g.Min(r => r.Before),
                    After = g.Max(r => r.After),
                }).ToList();
        }
        return ReputationMigration.Apply(profile, _revisions, () => Backup(profile));
    }

    private void Backup(PmcData profile)
    {
        var owner = saves.GetProfiles().Single(p => ReferenceEquals(p.Value.CharacterData?.PmcData, profile));
        var bytes = System.Text.Encoding.UTF8.GetBytes(json.Serialize(owner.Value)
            ?? throw new InvalidDataException("Could not serialize the profile before reputation migration."));
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var root = Path.GetFullPath("user/seasonal/progression-backups");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, owner.Key + "-" + hash + ".json");
        if (!File.Exists(path))
        {
            using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            output.Write(bytes);
            output.Flush(true);
        }
        if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) != hash)
        {
            throw new IOException("Reputation migration backup failed verification: " + owner.Key);
        }
    }
}
