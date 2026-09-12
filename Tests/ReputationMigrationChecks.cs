using Newtonsoft.Json;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Utils.Json;
using WTT.Campaigns.Server.Progression;

namespace WTT.Campaigns.Tests;

internal static class ReputationMigrationChecks
{
    internal static void Run(Action<bool, string> check)
    {
        const string quest = "596b36c586f77450d6045ad2";
        const string trader = "5ac3b934156ae10c4430e83c";
        var revisions = new List<ReputationRevision>
        {
            new() { QuestId = quest, RewardId = "be1f419866029c2192439c20", TraderId = trader, Before = 0, After = .5m },
        };
        PmcData Profile(QuestStatusEnum state)
        {
            return new()
            {
                Info = new() { Level = 15, Side = "Usec" },
                Quests = [new() { QId = quest, Status = state, StartTime = 123, StatusTimers = [] }],
                TradersInfo = new() { [new MongoId(trader)] = new() { Standing = .1, LoyaltyLevel = 1 } },
                ExtensionData = new(),
            };
        }
        var legacy = Profile(QuestStatusEnum.Success);
        var questBefore = JsonConvert.SerializeObject(legacy.Quests);
        var backups = 0;
        check(ReputationMigration.Apply(legacy, revisions, () => backups++), "Legacy completed quest receives its migration");
        check(Math.Abs(legacy.TradersInfo![trader].Standing!.Value - .6) < 1e-8 && backups == 1, "Supplier receives exactly .50 Ragman after backup");
        check(JsonConvert.SerializeObject(legacy.Quests) == questBefore, "Migration preserves quest statuses and timers");
        check(!ReputationMigration.Apply(legacy, revisions, () => backups++), "Repeated migration grants nothing");
        var options = new System.Text.Json.JsonSerializerOptions
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };
        foreach (var converter in new SptJsonConverterRegistrator().GetJsonConverters())
        {
            options.Converters.Add(converter);
        }
        var reloaded = System.Text.Json.JsonSerializer.Deserialize<PmcData>(System.Text.Json.JsonSerializer.Serialize(legacy, options), options)!;
        check(!ReputationMigration.Apply(reloaded, revisions, () => backups++), "Serialized receipt prevents duplicate credits after restart");
        foreach (var state in new[] { QuestStatusEnum.Started, QuestStatusEnum.Fail, QuestStatusEnum.AvailableForStart, QuestStatusEnum.Locked })
        {
            var fresh = Profile(state);
            check(ReputationMigration.Apply(fresh, revisions, () => throw new Exception("No credit needs a backup")), "Uncompleted quest records current reward revision");
            check(fresh.TradersInfo![trader].Standing == .1, "Uncompleted or failed quest receives no credit");
            fresh.Quests![0].Status = QuestStatusEnum.Success;
            fresh.TradersInfo[trader].Standing += .5;
            check(!ReputationMigration.Apply(fresh, revisions, () => backups++), "Future completion does not also earn a migration credit");
        }
        var next = new List<ReputationRevision>
        {
            new() { QuestId = quest, RewardId = revisions[0].RewardId, TraderId = trader, Before = 0, After = .7m },
        };
        check(ReputationMigration.Apply(reloaded, next, () => backups++), "New revision migrates a previously migrated character");
        check(Math.Abs(reloaded.TradersInfo![trader].Standing!.Value - .8) < 1e-8, "Later revision credits only the additional .20");
        check(!ReputationMigration.Apply(reloaded, revisions, () => backups++), "Downgrading does not subtract reputation or erase the newer receipt");
        var failedBackup = Profile(QuestStatusEnum.Success);
        var before = JsonConvert.SerializeObject(failedBackup);
        try
        {
            ReputationMigration.Apply(failedBackup, revisions, () => throw new IOException("Locked backup"));
            throw new Exception("Expected backup failure");
        }
        catch (IOException) { }
        check(JsonConvert.SerializeObject(failedBackup) == before, "Backup failure leaves both standing and receipt unchanged");
        var separate = Profile(QuestStatusEnum.Success);
        check(ReputationMigration.Apply(separate, revisions, () => backups++), "Separate campaign character receives its own migration");
        var missing = Profile(QuestStatusEnum.Success);
        missing.TradersInfo!.Clear();
        check(!ReputationMigration.Apply(missing, revisions, () => backups++), "Missing trader cannot consume a migration credit");
    }
}
