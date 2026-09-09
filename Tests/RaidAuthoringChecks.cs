using Newtonsoft.Json.Linq;
using SeasonalPerks.Server.Seasons;
using SeasonalPerks.Server.Web.Authoring;
using SeasonalPerks.Shared.Authoring;
using SeasonalPerks.Shared.Native;
using SeasonalPerks.Shared.Seasons;
using SeasonalPerks.Shared.Spatial;

namespace SeasonalPerks.Tests;

internal static class RaidAuthoringChecks
{
    public static void Run(SeasonRepository repository, Action<bool, string> check)
    {
        var service = new RaidAuthoringService(repository);
        var draft = repository.Create(false);
        var request = new AuthoringRequest
        {
            ClientId = Guid.NewGuid().ToString("N"),
            RaidId = Guid.NewGuid().ToString("N"),
            Location = "woods",
            Enabled = true,
        };
        var response = service.Poll("account", "character", request);
        check(
            response.Definition == null && service.Clients().Count == 1,
            "Authoring opt-in advertises presence without granting draft access"
        );
        Reject(() => service.Submit("account", "character", request), "Presence alone cannot submit draft edits");
        service.Connect(request.ClientId, draft.Id);
        response = service.Poll("account", "character", request);
        request.DraftId = response.DraftId;
        request.Grant = response.Grant;
        request.Revision = response.Revision;
        check(response.Definition?.Id == draft.Definition.Id, "Administrator connection grants the selected draft");
        Reject(() => service.Poll("other-account", "character", request), "A client identity cannot be claimed by another account");
        Reject(() => service.Submit("account", "different-character", request), "Grant cannot cross backend characters");

        var zone = new SeasonZone
        {
            Id = SeasonRepository.NewId(),
            Name = "Camp",
            Location = "woods",
            Scene = "woods_main",
        };
        request.Definition = SeasonCompiler.Copy(response.Definition!);
        request.Definition.Zones.Add(zone);
        request.Definition.Name = "Client cannot overwrite unrelated settings";
        request.OperationId = Guid.NewGuid().ToString("N");
        response = service.Submit("account", "character", request);
        check(
            response.Definition!.Zones.Count == 1
                && response.Definition.Name == draft.Definition.Name
                && response.Definition.FormatVersion == 2,
            "Spatial submission saves only authoring fields and promotes the pack format"
        );
        check(
            service.Submit("account", "character", request).Revision == response.Revision,
            "Duplicate authoring operation returns its original receipt"
        );
        request.Definition.Zones[0].Name = "Mutated retry";
        Reject(() => service.Submit("account", "character", request), "Operation IDs reject changed retry payloads");
        var baseline = SeasonCompiler.Copy(response.Definition);
        var web = SeasonCompiler.Copy(baseline);
        web.Description = "Edited in browser";
        service.Save(draft.Id, baseline, web);
        var client = SeasonCompiler.Copy(baseline);
        client.Zones[0].Radius = 9;
        var merged = service.Save(draft.Id, baseline, client);
        check(
            merged.Conflicts.Count == 0 && merged.Definition!.Description == web.Description && merged.Definition.Zones[0].Radius == 9,
            "Independent browser and client edits merge: "
                + Newtonsoft.Json.JsonConvert.SerializeObject(
                    new
                    {
                        merged.Conflicts,
                        merged.Definition!.Description,
                        merged.Definition.Zones,
                    }
                )
        );
        var local = SeasonCompiler.Copy(merged.Definition!);
        var remote = SeasonCompiler.Copy(merged.Definition!);
        local.Zones[0].Name = "Local";
        remote.Zones[0].Name = "Remote";
        service.Save(draft.Id, merged.Definition!, remote);
        var conflict = service.Save(draft.Id, merged.Definition!, local);
        check(
            conflict.Conflicts.Count == 1
                && conflict.Candidate!.Zones[0].Name == "Local"
                && conflict.RemoteCandidate!.Zones[0].Name == "Remote",
            "Conflicts return both explicit choices without overwriting the draft"
        );
        check(repository.Load(draft.Id).Definition.Zones[0].Name == "Remote", "Conflicts do not persist partial changes");
        var resolved = service.Save(draft.Id, conflict.Definition!, conflict.Candidate!);
        check(
            resolved.Conflicts.Count == 0 && resolved.Definition!.Zones[0].Name == "Local",
            "Choosing a conflict version uses the current server baseline"
        );

        var invalid = SeasonCompiler.Copy(resolved.Definition!);
        invalid.Zones[0].Size.X = float.NaN;
        Reject(() => service.Save(draft.Id, resolved.Definition!, invalid), "Non-finite geometry is rejected");
        invalid = SeasonCompiler.Copy(resolved.Definition!);
        invalid.Zones[0].Radius = 0;
        Reject(() => service.Save(draft.Id, resolved.Definition!, invalid), "Zero-size geometry is rejected");

        var withQuest = SeasonCompiler.Copy(resolved.Definition!);
        var quest = NativeQuestAuthoring.Create();
        var condition = NativeQuestAuthoring.Condition("VisitPlace");
        quest.Conditions.AvailableForFinish.Add(condition);
        withQuest.Quests.Add(quest);
        service.Save(draft.Id, resolved.Definition!, withQuest);
        request.Definition = null;
        response = service.Poll("account", "character", request);
        request.Revision = response.Revision;
        service.Request(
            draft.Id,
            new()
            {
                Tool = "Zone",
                TargetKind = "Condition",
                TargetId = condition.Id,
            }
        );
        response = service.Poll("account", "character", request);
        var task = response.Tasks.Single();
        request.TaskId = task.Id;
        request.TaskStatus = "Completed";
        request.ResultId = zone.Id;
        request.Definition = repository.Load(draft.Id).Definition;
        request.OperationId = Guid.NewGuid().ToString("N");
        response = service.Submit("account", "character", request);
        var assigned = SpatialRules.Conditions(response.Definition!).Single(c => c.Id == condition.Id);
        check(
            assigned.Target?.Values.Single() == zone.Id && assigned.ZoneId == null,
            "VisitPlace captures populate the native target field"
        );
        check(service.Clients().Single().Tasks.Single().Status == "Completed", "Successful capture completion is acknowledged");
        var removed = SeasonCompiler.Copy(response.Definition!);
        removed.Zones.Clear();
        Reject(() => service.Save(draft.Id, response.Definition!, removed), "Referenced zones cannot be deleted");
        var inZone = NativeQuestAuthoring.Condition("InZone");
        SpatialRules.Assign(inZone, zone.Id);
        check(inZone.ZoneIds?.Single() == zone.Id && inZone.ZoneId == null, "InZone captures populate native zoneIds");
        var leave = NativeQuestAuthoring.Condition("LeaveItemAtLocation");
        SpatialRules.Assign(leave, zone.Id);
        check(leave.ZoneId == zone.Id && leave.PlantTime == 10, "Placement captures preserve item-placement timing");
        var copy = SeasonRepository.Duplicate(response.Definition!);
        check(
            copy.Zones[0].Id != zone.Id && SpatialRules.Conditions(copy).Any(c => SpatialRules.References(c).Contains(copy.Zones[0].Id)),
            "Season duplication replaces zones and repairs native references"
        );
        var packDraft = repository.Create(true, resolved.Definition!);
        var asset = repository.AddImage(
            Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==")
        );
        packDraft.Definition.UniversalImage = packDraft.Definition.UniversalUnavailableImage = asset;
        packDraft.Definition.Documents[0].Image = packDraft.Definition.Documents[0].UnavailableImage = asset;
        packDraft = repository.Save(packDraft);
        var pack = repository.Publish(packDraft, SeasonValidator.Validate(packDraft.Definition));
        check(
            repository.Pack(pack).Zones.Count == 1 && repository.Pack(pack).FormatVersion == 2,
            "Spatial pack export and reload preserve format 2"
        );
        var imported = repository.Import(repository.Export(pack));
        check(imported.Definition.Zones[0].Name == packDraft.Definition.Zones[0].Name, "Spatial pack import preserves geometry");

        request.RaidId = Guid.NewGuid().ToString("N");
        request.Definition = null;
        check(service.Poll("account", "character", request).Grant.Length == 0, "Changing raids expires grants and capture tasks");
        service.Connect(request.ClientId, draft.Id);
        service.UtcNow = () => DateTimeOffset.UtcNow.AddMinutes(1);
        check(service.Clients().Count == 0, "Lost client presence expires without waiting for another request");
        Reject(() => service.Submit("account", "character", request), "Expired raids cannot replay captures");

        var conflicts = new List<DraftConflict>();
        var a = JObject.Parse("{ 'Zones': [ { 'Id': 'a', 'Name': 'A' } ] }");
        var b = JObject.Parse("{ 'Zones': [ { 'Id': 'b', 'Name': 'B' } ] }");
        var both = DraftMerge.Merge(new JObject(), a, b, conflicts)!;
        check(conflicts.Count == 0 && both["Zones"]!.Count() == 2, "Concurrent first-time zone additions merge by identity");

        void Reject(Action action, string name)
        {
            try
            {
                action();
            }
            catch (Exception e) when (e is InvalidOperationException or InvalidDataException)
            {
                check(true, name);
                return;
            }
            check(false, name);
        }
    }
}
