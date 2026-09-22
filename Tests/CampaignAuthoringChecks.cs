using Newtonsoft.Json;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Json;
using WTT.Campaigns.Server.Editor;
using WTT.Campaigns.Server.Profiles;
using WTT.Campaigns.Server.Seasons;
using WTT.Campaigns.Shared.Authoring;
using WTT.Campaigns.Shared.Hub;
using WTT.Campaigns.Shared.Missions;
using WTT.Campaigns.Shared.Native;
using WTT.Campaigns.Shared.Profiles;
using WTT.Campaigns.Shared.Seasons;
using Path = System.IO.Path;

namespace WTT.Campaigns.Tests;

internal static class CampaignAuthoringChecks
{
    internal static void Repository(SeasonRepository store, DraftEnvelope draft, string pack, Action<bool, string> check)
    {
        var count = store.Drafts().Count;
        var resumed = store.EditPublished(pack);
        check(
            resumed.Id == store.EditPublished(pack).Id && store.Drafts().Count == count && resumed.Definition.Id == draft.Definition.Id,
            "Editing published campaign resumes the existing working draft"
        );
        var before = store.Pack(pack);
        resumed.Definition.Collection.DocumentLimit++;
        resumed = store.Save(resumed);
        var next = store.Publish(resumed, SeasonValidator.Validate(resumed.Definition));
        check(
            store.Pack(next).Revision == before.Revision + 1 && store.Pack(next).Id == before.Id,
            "An already-used campaign publishes a new release under the same identity"
        );
        check(store.Pack(pack).Collection.DocumentLimit == before.Collection.DocumentLimit, "Published historical release stays intact");
        check(store.RevisionCandidates(pack).First() == next, "Existing selected pack resolves newest revision first");
        var ids = new Dictionary<string, string>();
        var original = JsonConvert.SerializeObject(resumed.Definition);
        var test = SeasonRepository.Duplicate(resumed.Definition, ids);
        var refresh = SeasonRepository.Duplicate(resumed.Definition, ids);
        check(
            test.Id != resumed.Definition.Id && test.Id == refresh.Id && test.Items[0].Id == refresh.Items[0].Id,
            "Test identity map isolates content and remains stable on refresh"
        );
        check(
            test.Missions.Count == 0 && test.Story == null && test.MapLayouts.Count == 0,
            "Campaign testing does not invent missions, story, or map layouts"
        );
        check(JsonConvert.SerializeObject(resumed.Definition) == original, "Testing leaves authoring source untouched");
    }

    internal static void Run(Action<bool, string> check)
    {
        var json = new JsonUtil([new SptJsonConverterRegistrator()]);
        string Id() => SeasonRepository.NewId();
        var request = new CampaignTestRequest
        {
            Action = "apply",
            ExpectedLoadedRevision = 2,
            ExpectedDraftRevision = 3,
            OperationId = Guid.NewGuid().ToString("N"),
        };
        CampaignTestPolicy.RequireRequest(request, "apply");
        CampaignTestPolicy.RequireUpdate(request, 2, 3, false);
        void Reject(Action action, string message)
        {
            var rejected = false;
            try
            {
                action();
            }
            catch (InvalidOperationException)
            {
                rejected = true;
            }
            check(rejected, message);
        }
        Reject(() => CampaignTestPolicy.RequireUpdate(request, 3, 3, false), "Stale loaded test revision is rejected");
        Reject(() => CampaignTestPolicy.RequireUpdate(request, 2, 4, false), "Concurrent draft edits are rejected");
        Reject(() => CampaignTestPolicy.RequireUpdate(request, 2, 3, true), "Test refresh is forbidden during a raid");
        Reject(() => CampaignTestPolicy.RequireRequest(request, "reset"), "Control action must match its endpoint");
        request.Version = 1;
        Reject(() => CampaignTestPolicy.RequireRequest(request, "apply"), "Retired disposable-test protocol is rejected");
        var directory = Path.Combine(Path.GetTempPath(), "wtt-campaign-persistence-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new CampaignTestStorage(directory);
            var record = new CampaignTestRecord
            {
                Owner = Id(),
                DraftId = Id(),
                ProfileId = Id(),
                SourceRevision = 3,
                Definition = new() { Id = Id(), Name = "Loaded snapshot" },
                ProfileJson = "{\"progress\":42}",
                Identities = new() { [Id()] = Id() },
            };
            store.Save(record);
            var active = record;
            var pending = SeasonCompiler.Copy(record);
            pending.SourceRevision++;
            pending.ProfileJson = "{\"progress\":99}";
            try
            {
                CampaignTestUpdateTransaction.Commit(
                    () => active = pending,
                    () => throw new IOException("simulated write failure"),
                    () => active = record
                );
            }
            catch (IOException) { }
            check(
                ReferenceEquals(active, record) && store.Read(record.Owner, record.DraftId)!.SourceRevision == 3,
                "Interrupted refresh restores runtime and retains the durable snapshot"
            );
            var persisted = false;
            try
            {
                CampaignTestUpdateTransaction.Commit(
                    () =>
                    {
                        active = pending;
                        throw new InvalidOperationException("invalid content");
                    },
                    () => persisted = true,
                    () => active = record
                );
            }
            catch (InvalidOperationException) { }
            check(!persisted && ReferenceEquals(active, record), "Registration failure restores old content before any save");
            var loaded = new CampaignTestStorage(directory).Read(record.Owner, record.DraftId)!;
            check(
                loaded.ProfileJson == record.ProfileJson && loaded.Definition.Name == "Loaded snapshot" && loaded.SourceRevision == 3,
                "Restart reloads character and the exact tested snapshot together"
            );
            check(loaded.Identities.SequenceEqual(record.Identities), "Restart preserves stable test identity mapping");
            check(store.Read(Id(), record.DraftId) == null, "Another account cannot resolve the saved test");
            var reset = SeasonCompiler.Copy(record);
            reset.ProfileId = Id();
            reset.RetiredProfiles.Add(record.ProfileId);
            reset.ProfileJson = "{\"progress\":0}";
            store.Save(reset);
            var rejected = false;
            try
            {
                store.Save(record);
            }
            catch (InvalidOperationException)
            {
                rejected = true;
            }
            check(
                rejected && store.Read(reset.Owner, reset.DraftId)!.ProfileId == reset.ProfileId,
                "Late save cannot resurrect a reset character"
            );
            check(
                File.Exists(Path.Combine(directory, record.Owner, record.DraftId + ".json.bak")),
                "Atomic test replacement retains backup"
            );
            rejected = false;
            try
            {
                store.Read("../escape", record.DraftId);
            }
            catch (InvalidDataException)
            {
                rejected = true;
            }
            check(rejected, "Test storage rejects path traversal identities");
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }

        var campaign = new SeasonDefinition
        {
            Id = Id(),
            BattlePassId = Id(),
            Revision = 1,
        };
        var questId = Id();
        var doneId = Id();
        var unchanged = Id();
        var changed = Id();
        var quest = new NativeQuest
        {
            Id = questId,
            Conditions = new()
            {
                AvailableForFinish =
                [
                    new()
                    {
                        Id = unchanged,
                        ConditionType = "Level",
                        Value = 2,
                    },
                    new()
                    {
                        Id = changed,
                        ConditionType = "Level",
                        Value = 3,
                    },
                ],
            },
        };
        campaign.Quests.Add(quest);
        campaign.Quests.Add(new() { Id = doneId });
        var receipt = Id();
        var pmc = new PmcData
        {
            ExtensionData = new(),
            Quests =
            [
                new()
                {
                    StartTime = 0,
                    StatusTimers = new(),
                    QId = questId,
                    Status = QuestStatusEnum.AvailableForFinish,
                    CompletedConditions = [new MongoId(unchanged), new MongoId(changed)],
                },
                new()
                {
                    StartTime = 0,
                    StatusTimers = new(),
                    QId = doneId,
                    Status = QuestStatusEnum.Success,
                    CompletedConditions = [],
                },
            ],
        };
        pmc.ExtensionData["wttCampaignsState"] = JsonConvert.SerializeObject(
            new PerkState
            {
                SeasonId = campaign.Id,
                Revision = 4,
                AppliedGrants = [receipt],
                GameplayHash = SeasonRepository.GameplayHash(campaign),
            }
        );
        var hubKey = "wttCampaignsHub:" + campaign.Id;
        pmc.ExtensionData[hubKey] = JsonConvert.SerializeObject(
            new HubProgress
            {
                SeasonId = campaign.Id,
                Claimed = [receipt],
                Tarcoins = 100,
            }
        );
        CampaignReconciliation.Apply(pmc, campaign, json, campaign);
        check(pmc.Quests[0].CompletedConditions!.Count == 2, "Baseline capture preserves both objectives");
        var baseline = SeasonCompiler.Copy(campaign);
        campaign.Revision++;
        campaign.Quests[0].Conditions.AvailableForFinish[1].Value = 5;
        var normal = JsonConvert.SerializeObject(pmc);
        var staged = json.Deserialize<PmcData>(json.Serialize(pmc)!)!;
        check(staged.Quests[0].QId.ToString() == questId, "Native serialization preserves quest identity during staging");
        check(CampaignReconciliation.Apply(staged, campaign, json, baseline), "Changed campaign triggers reconciliation");
        check(JsonConvert.SerializeObject(pmc) == normal, "Staging reconciliation leaves the original profile untouched");
        check(
            staged.Quests[0].CompletedConditions!.Select(c => c.ToString()).SequenceEqual(new[] { unchanged }),
            "Compatible objectives retain progress; changed objectives reset: "
                + string.Join(",", staged.Quests[0].CompletedConditions!)
                + " expected "
                + unchanged
        );
        check(
            staged.Quests[0].Status == QuestStatusEnum.Started && staged.Quests[1].Status == QuestStatusEnum.Success,
            "Changed unfinished quest reopens while completed quest remains complete"
        );
        check(
            staged.ExtensionData[hubKey].ToString() == pmc.ExtensionData[hubKey].ToString()
                && ProfileStateSerialization.Read<PerkState>(staged, "wttCampaignsState")!.AppliedGrants.Contains(receipt),
            "Campaign update preserves currency, claimed rewards and one-time grants"
        );
        check(!CampaignReconciliation.Apply(staged, campaign, json), "Reconciliation is retry-safe");
        var removed = SeasonCompiler.Copy(campaign);
        removed.Quests.Clear();
        CampaignReconciliation.Apply(staged, removed, json, campaign);
        check(staged.Quests.Count == 0, "Removed quests leave active progression");
        CampaignReconciliation.Apply(staged, campaign, json, removed);
        check(
            staged.Quests.Single(q => q.QId.ToString() == doneId).Status == QuestStatusEnum.Success
                && staged.Quests.Single(q => q.QId.ToString() == questId).Status == QuestStatusEnum.Started,
            "Reintroduced quests retain completion and acceptance, preventing reward replay"
        );

        var unknown = json.Deserialize<PmcData>(json.Serialize(pmc)!)!;
        CampaignReconciliation.Apply(unknown, campaign, json);
        check(unknown.Quests[0].CompletedConditions!.Count == 1, "Stored compatibility stamp survives without a historical pack");
        unknown.ExtensionData.Remove("wttCampaignsContent");
        CampaignReconciliation.Apply(unknown, campaign, json);
        check(
            unknown.Quests[0].CompletedConditions!.Count == 0 && unknown.Quests[1].Status == QuestStatusEnum.Success,
            "Unknown historical baseline preserves completion and resets unfinished progress conservatively"
        );

        var mission = new MissionDefinition { Id = Id(), LayoutId = Id() };
        campaign.Missions.Add(mission);
        campaign.MapLayouts.Add(new() { Id = mission.LayoutId, Location = "bigmap" });
        CampaignReconciliation.Apply(staged, campaign, json);
        var progress = new MissionProgress
        {
            SeasonId = campaign.Id,
            CompletedMissionIds = [Id()],
            ActiveRun = new()
            {
                MissionId = mission.Id,
                Status = MissionRunStatuses.Prepared,
                CheckpointId = Id(),
                ContentRevision = campaign.Revision,
                ContentHash = SeasonRepository.GameplayHash(campaign),
                ContextVersion = 3,
                Scope = campaign.Id,
            },
        };
        var checkpoint = progress.ActiveRun.CheckpointId;
        var missionKey = "wttCampaignsMissions:" + campaign.Id;
        staged.ExtensionData[missionKey] = JsonConvert.SerializeObject(progress);
        campaign.Collection.DocumentLimit++;
        campaign.Revision++;
        CampaignReconciliation.Apply(staged, campaign, json);
        var updated = ProfileStateSerialization.Read<MissionProgress>(staged, missionKey)!;
        check(
            updated.ActiveRun!.CheckpointId == checkpoint && updated.ActiveRun.ContentRevision == campaign.Revision,
            "An unchanged mission keeps its checkpoint when unrelated campaign content changes"
        );
        mission.TimeLimitMinutes = 10;
        CampaignReconciliation.Apply(staged, campaign, json);
        updated = ProfileStateSerialization.Read<MissionProgress>(staged, missionKey)!;
        check(
            updated.ActiveRun!.Status == MissionRunStatuses.Cancelled
                && updated.ActiveRun.CheckpointId.Length == 0
                && updated.CompletedMissionIds.SetEquals(progress.CompletedMissionIds),
            "Changed mission invalidates obsolete checkpoint without erasing completed missions"
        );
    }
}
