using Newtonsoft.Json;
using WTT.Campaigns.Client.Authoring;
using WTT.Campaigns.Server.Seasons;
using WTT.Campaigns.Shared.Seasons;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Tests;

internal sealed class SceneCatalogChecks : WTT.Campaigns.Server.Editor.EditorSessionRegistry
{
    internal static void Protocol(SeasonRepository repository, WTT.Campaigns.Server.Seasons.DraftEnvelope draft, Action<bool, string> check)
    {
        var owner = SeasonRepository.NewId();
        var profile = SeasonRepository.NewId();
        var session = new Session
        {
            Owner = owner,
            Profile = profile,
            Ready = true,
            Location = "woods",
            Draft = draft.Id,
        };
        Profiles[profile] = session;
        try
        {
            var service = new WTT.Campaigns.Server.Web.Authoring.RaidAuthoringService(repository);
            var request = new WTT.Campaigns.Shared.Authoring.AuthoringRequest
            {
                Version = 2,
                EditorSessionId = session.Id,
                ClientId = Guid.NewGuid().ToString("N"),
                RaidId = Guid.NewGuid().ToString("N"),
                Location = "woods",
                Enabled = true,
            };
            service.Poll(owner, profile, request);
            service.Connect(request.ClientId, draft.Id);
            var response = service.Poll(owner, profile, request);
            request.DraftId = response.DraftId;
            request.Revision = response.Revision;
            request.Grant = response.Grant;
            request.Definition = SeasonCompiler.Copy(response.Definition!);
            request.Definition.MapLayouts[0].Loot.Clear();
            request.OperationId = Guid.NewGuid().ToString("N");
            var denied = false;
            try
            {
                service.Submit(owner, profile, request);
            }
            catch (InvalidOperationException e)
            {
                denied = e.Message.Contains("Update the client");
            }
            check(
                denied && repository.Load(draft.Id).Definition.MapLayouts[0].Loot.Count == 1,
                "Version 2 editor submissions cannot silently erase format 5 records"
            );
            request.Version = 3;
            request.OperationId = Guid.NewGuid().ToString("N");
            request.Definition = SeasonCompiler.Copy(response.Definition!);
            request.Definition.MapLayouts[0].Loot[0].Position.X = 12;
            var updated = service.Submit(owner, profile, request);
            check(
                updated.Error == null && updated.Definition?.MapLayouts[0].Loot[0].Position.X == 12,
                "Version 3 editor submissions persist actual loot transforms"
            );
        }
        finally
        {
            Profiles.TryRemove(profile, out _);
        }
    }

    internal static void Run(Action<bool, string> check)
    {
        var layout = MapEditorChecks.Example();
        var original = new MapTarget
        {
            Kind = "Container",
            Scene = "woods_main",
            Path = "box[0]",
            NativeId = "native-box",
            Template = "box",
            Fingerprint = new string('A', 64),
            Origin = new() { X = 10 },
        };
        layout.Objects.Add(
            new MapObjectEdit
            {
                Id = SeasonRepository.NewId(),
                Name = "Container",
                Location = layout.Location,
                Scene = original.Scene,
                Target = original,
                Operation = "Hide",
            }
        );
        layout.Loot.Add(
            new MapLootPlacement
            {
                Id = SeasonRepository.NewId(),
                Name = "Placed item",
                Location = layout.Location,
                Scene = original.Scene,
                Items = [new() { Id = SeasonRepository.NewId(), Template = "5447a9cd4bdc2dbd208b4567" }],
            }
        );
        check(
            MapLayoutRules.Errors(layout).Count == 0 && MapLayoutRules.Format([layout]) == 5,
            "Native targets and placed loot require format 5"
        );
        var copy = JsonConvert.DeserializeObject<MapLayout>(JsonConvert.SerializeObject(layout))!;
        check(
            copy.Loot[0].Items[0].Template == layout.Loot[0].Items[0].Template && copy.Objects[0].Target.NativeId == "native-box",
            "Layout roundtrip preserves native and item identities"
        );
        check(MapLayoutRules.OwnedIds(copy).Contains(copy.Loot[0].Items[0].Id), "Placed item tree IDs participate in campaign duplication");
        copy.Objects[0].Operation = "Copy";
        check(MapLayoutRules.Errors(copy).Any(e => e.Contains("Only static props")), "Native gameplay containers cannot be cloned");
        copy.Objects[0].Operation = "Hide";
        copy.Loot[0].Items[0].ParentId = copy.Loot[0].Items[0].Id;
        check(MapLayoutRules.Errors(copy).Count > 0, "Cyclic placed item trees are rejected");
        copy = SeasonCompiler.Copy(layout);
        copy.Objects[0].Target.Origin.X = float.NaN;
        check(MapLayoutRules.Errors(copy).Any(e => e.Contains("original loot position")), "Nonfinite target anchors are rejected");
        var actual = SeasonCompiler.Copy(original);
        actual.Path = "box[9]";
        check(SceneTargetRules.Matches(original, actual), "Stable container identity survives sibling-index changes");
        actual.NativeId = "different";
        check(!SceneTargetRules.Matches(original, actual), "Similar containers do not replace a missing target");
        original.Kind = actual.Kind = "Loot";
        original.NativeId = actual.NativeId = "";
        actual.Origin.X = 10.1f;
        check(SceneTargetRules.Matches(original, actual), "Loose loot without a spawn ID uses template, structure and original position");
        actual.Origin.X = 11;
        check(!SceneTargetRules.Matches(original, actual), "Distant identical loot is not silently rebound");
        actual = SeasonCompiler.Copy(original);
        check(
            new[] { actual, actual }.Count(t => SceneTargetRules.Matches(original, t)) == 2,
            "Ambiguous loot produces multiple candidates and must not be resolved"
        );
        actual.Template = "other";
        check(!SceneTargetRules.Matches(original, actual), "A changed randomized item cannot inherit a prior loot edit");
        check(
            MapLayoutRules.Errors(new MapLayout { Loot = null! }).Any(e => e.Contains("null")),
            "Null loot collections return a validation error"
        );
        Loads(check).GetAwaiter().GetResult();
    }

    private static async Task Loads(Action<bool, string> check)
    {
        var released = new List<string>();
        var pending = new TaskCompletionSource<string>();
        var lease = new SceneModelLease<string>(released.Add);
        var loading = lease.Load(_ => pending.Task);
        check(lease.Pending, "Model loads expose a pending state before walkthrough");
        lease.Dispose();
        pending.SetResult("late result");
        await loading;
        check(
            lease.Model == null && released.SequenceEqual(["late result"]),
            "Canceled map load releases a late native model exactly once"
        );
        lease.Dispose();
        check(released.Count == 1, "Repeated teardown cannot release pooled models twice");
        var next = new SceneModelLease<string>(released.Add);
        await next.Load(_ => Task.FromResult("second map"));
        check(next.Model == "second map" && !next.Pending, "A new map accepts its own completed model");
        next.Dispose();
        check(released.Count == 2, "Map unload releases its owned completed model");
        var failed = new SceneModelLease<string>(released.Add);
        await failed.Load(_ => Task.FromException<string>(new InvalidOperationException("Missing bundle")));
        check(failed.Error == "Missing bundle" && !failed.Pending, "Missing assets remain actionable without an endless loading state");
        failed.Dispose();
    }
}
