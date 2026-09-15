using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WTT.Campaigns.Server.Editor;
using WTT.Campaigns.Server.Seasons;
using WTT.Campaigns.Shared.Authoring;
using WTT.Campaigns.Shared.Seasons;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Tests;

internal sealed class MapEditorChecks : EditorSessionRegistry
{
    internal static MapLayout Example()
    {
        MapVolume Volume(string name) =>
            new()
            {
                Id = SeasonRepository.NewId(),
                Name = name,
                Location = "woods",
                Scene = "woods_main",
            };
        return new MapLayout
        {
            Id = SeasonRepository.NewId(),
            Name = "Building escape",
            Location = "woods",
            Start = new SpatialCapture
            {
                Id = SeasonRepository.NewId(),
                Name = "Start",
                Location = "woods",
                Scene = "woods_main",
            },
            Exit = Volume("Exit"),
            Checkpoints = new() { Volume("Hall"), Volume("Stairs") },
            Barriers = new() { Volume("Blocked passage") },
        };
    }

    internal static void Run(Action<bool, string> check)
    {
        var nativeSave = typeof(SPTarkov.Server.Core.Servers.SaveServer).GetMethod("SaveProfileAsync")!;
        check(
            nativeSave.ReturnType == typeof(Task<long>) && nativeSave.GetParameters()[0].Name == "sessionID",
            "Scratch save gate matches the reference-package save contract"
        );
        var equipment = SeasonRepository.NewId();
        var pockets = SeasonRepository.NewId();
        var scratch = new SPTarkov.Server.Core.Models.Eft.Common.PmcData
        {
            Inventory = new()
            {
                Equipment = new SPTarkov.Server.Core.Models.Common.MongoId(equipment),
                Items = new()
                {
                    new() { Id = new(equipment) },
                    new()
                    {
                        Id = new(pockets),
                        ParentId = equipment,
                        SlotId = "Pockets",
                    },
                    new()
                    {
                        Id = new(SeasonRepository.NewId()),
                        ParentId = equipment,
                        SlotId = "FirstPrimaryWeapon",
                    },
                    new()
                    {
                        Id = new(SeasonRepository.NewId()),
                        ParentId = pockets,
                        SlotId = "main",
                    },
                },
            },
        };
        EditorScratchInventory.Prepare(scratch);
        check(
            scratch.Inventory.Items!.Count() == 2 && scratch.Inventory.Items.Any(i => i.SlotId == "Pockets"),
            "Scratch loadout retains native traversal pockets while removing weapons and pocket contents"
        );
        EditorScratchInventory.Prepare(scratch);
        check(
            scratch.Inventory.Items.Count() == 2 && scratch.Inventory.FastPanel!.Count == 0 && scratch.Quests!.Count == 0,
            "Scratch inventory preparation is repeatable and clears gameplay bindings"
        );
        var layout = Example();
        check(MapLayoutRules.Errors(layout, true).Count == 0, "Complete route is ready for scene resolution");
        var copy = SeasonCompiler.Copy(layout);
        copy.Start = null;
        check(
            MapLayoutRules.Errors(copy).Count == 0 && MapLayoutRules.Errors(copy, true).Count > 0,
            "Incomplete layouts save as drafts but cannot start walkthrough"
        );
        copy = SeasonCompiler.Copy(layout);
        copy.Checkpoints[1].Id = copy.Checkpoints[0].Id;
        check(MapLayoutRules.Errors(copy).Count > 0, "Duplicate route identities are rejected");
        copy = SeasonCompiler.Copy(layout);
        copy.Barriers[0].Size.X = float.NaN;
        check(MapLayoutRules.Errors(copy).Count > 0, "Nonfinite scene geometry cannot be applied");
        copy = SeasonCompiler.Copy(layout);
        copy.Barriers[0].Location = "factory4_day";
        check(MapLayoutRules.Errors(copy).Count > 0, "Map records cannot cross map identity");
        copy = SeasonCompiler.Copy(layout);
        copy.Objects.Add(new MapObjectEdit { Target = null! });
        check(MapLayoutRules.Errors(copy).Count > 0, "Missing target data produces validation errors instead of an exception");
        copy = SeasonCompiler.Copy(layout);
        copy.Checkpoints.Add(null!);
        check(MapLayoutRules.Errors(copy).Count > 0, "Null imported route records are rejected");
        var season = new SeasonDefinition
        {
            Id = SeasonRepository.NewId(),
            FormatVersion = 4,
            MapLayouts = new() { layout },
        };
        var duplicated = SeasonRepository.Duplicate(season);
        check(
            !MapLayoutRules.OwnedIds(duplicated.MapLayouts[0]).Intersect(MapLayoutRules.OwnedIds(layout)).Any(),
            "Campaign duplication replaces every map and route identity"
        );
        check(
            duplicated.MapLayouts[0].Checkpoints.Select(p => p.Name).SequenceEqual(layout.Checkpoints.Select(p => p.Name)),
            "Duplication preserves route order"
        );
        check(
            JToken.DeepEquals(JObject.FromObject(season), JObject.FromObject(SeasonCompiler.Copy(season))),
            "Format 4 map data survives serialization without losses"
        );
        var local = SeasonCompiler.Copy(season);
        var remote = SeasonCompiler.Copy(season);
        local.MapLayouts[0].Checkpoints[0].Position.X = 7;
        remote.MapLayouts[0].Barriers[0].Size.Y = 5;
        var conflicts = new List<DraftConflict>();
        var merged = DraftMerge
            .Merge(JObject.FromObject(season), JObject.FromObject(local), JObject.FromObject(remote), conflicts)!
            .ToObject<SeasonDefinition>()!;
        check(
            conflicts.Count == 0 && merged.MapLayouts[0].Checkpoints[0].Position.X == 7 && merged.MapLayouts[0].Barriers[0].Size.Y == 5,
            "Concurrent edits to independent map records merge"
        );
        remote.MapLayouts[0].Checkpoints[0].Position.X = 8;
        conflicts.Clear();
        DraftMerge.Merge(JObject.FromObject(season), JObject.FromObject(local), JObject.FromObject(remote), conflicts);
        check(conflicts.Count > 0, "Concurrent conflicting transforms require reconciliation");

        var state = new List<int>();
        var transaction = new SceneEditTransaction();
        transaction.Apply(() => state.Add(1), () => state.Remove(1));
        try
        {
            transaction.Apply(
                () =>
                {
                    state.Add(2);
                    throw new InvalidOperationException("apply failed");
                },
                () => state.Remove(2)
            );
        }
        catch (InvalidOperationException) { }
        check(state.Count == 0, "Partial scene application restores both completed and failing edits");
        transaction.Dispose();
        transaction.Dispose();
        check(state.Count == 0, "Repeated preview teardown is harmless");
        transaction.Apply(() => state.Add(3), () => state.Remove(3));
        transaction.Apply(() => { }, () => throw new InvalidOperationException("destroyed target"));
        try
        {
            transaction.Dispose();
        }
        catch (AggregateException) { }
        check(state.Count == 0, "A broken restore does not prevent restoring remaining targets");

        foreach (
            var path in new[]
            {
                "/client/game/profile/items/moving",
                "/wtt-campaigns/snapshot",
                "/wtt-campaigns/switch",
                "/wtt-campaigns/story/act",
                "/client/quest/accept",
            }
        )
            check(EditorPolicy.Blocks(path), "Editor blocks gameplay request " + path);
        foreach (
            var path in new[]
            {
                "/wtt-campaigns/editor/begin",
                "/wtt-campaigns/editor/end",
                "/client/game/start",
                "/client/quest/list",
                "/client/locations",
                "/client/hideout/settings",
                "/client/profile/status",
            }
        )
            check(!EditorPolicy.Blocks(path), "Editor permits native setup/read request " + path);

        var profile = SeasonRepository.NewId();
        var owner = SeasonRepository.NewId();
        var normal = SeasonRepository.NewId();
        var session = new Session
        {
            Profile = profile,
            Owner = owner,
            ReturnProfile = normal,
            Ready = true,
        };
        try
        {
            void Reject(Action action, string name)
            {
                var denied = false;
                try
                {
                    action();
                }
                catch (InvalidOperationException)
                {
                    denied = true;
                }
                check(denied, name);
            }
            Reject(() => session.OpenMap("woods"), "Editor cannot load a map before draft selection");
            session.Select("draft-a", "layout-a");
            session.OpenMap("woods");
            Reject(() => session.Select("draft-b", ""), "Draft switching cannot interrupt an active editing map");
            Reject(() => session.OpenMap("factory4_day"), "A second map requires cleanup of the first");
            Reject(session.CheckCanEnd, "Editor cannot reconnect the normal character while a map is active");
            session.UnloadMap();
            session.UnloadMap();
            check(
                session.Draft == "draft-a" && session.Layout == "layout-a",
                "Failed loading and repeated cleanup retain the selected draft and layout"
            );
            session.OpenMap("factory4_day");
            session.UnloadMap();
            session.CheckCanEnd();
            check(session.ReturnProfile == normal, "Consecutive maps preserve the normal character selected before entry");
            session.Ready = false;
            Reject(() => session.OpenMap("woods"), "Partial editor initialization cannot launch a map");
            session.Ready = true;
            check(session.Accepts(owner, session.Id, session.Contact), "Active editor accepts its owner and token");
            check(!session.Accepts(normal, session.Id, session.Contact), "Editor token cannot cross accounts");
            check(!session.Accepts(owner, Guid.NewGuid().ToString("N"), session.Contact), "Stale editor token is rejected");
            check(!session.Accepts(owner, session.Id, session.Contact.AddMinutes(2)), "Abandoned editor lease expires");
            ScratchIds[profile] = 0;
            Profiles[profile] = session;
            var now = DateTimeOffset.UtcNow;
            session.Contact = now;
            session.OpenMap("woods");
            foreach (var identity in new[] { owner, normal, profile })
            {
                var resolved = Resolve(identity, session.Id, now);
                check(
                    resolved.Accepted && ReferenceEquals(resolved.Session, session),
                    "Authenticated editor identity resolves " + identity
                );
            }
            var foreign = Resolve(SeasonRepository.NewId(), session.Id, now);
            check(
                !foreign.Accepted && foreign.Session == null && foreign.Status == ResolutionStatus.MissingIdentity,
                "A foreign transport identity cannot use a matching editor token"
            );
            session.Ready = false;
            check(
                Resolve(owner, session.Id, now).Status == ResolutionStatus.NotReady,
                "Unready editor sessions cannot authorize playtests"
            );
            session.Ready = true;
            session.Contact = now.AddMinutes(-2);
            check(Resolve(owner, session.Id, now).Status == ResolutionStatus.Expired, "Expired editor sessions cannot authorize playtests");
            session.Contact = now;
            session.UnloadMap();
            check(
                Resolve(owner, session.Id, now).Status == ResolutionStatus.MissingMap,
                "An unloaded editor map cannot authorize playtests"
            );
            session.OpenMap("woods");
            check(
                Resolve(owner, Guid.NewGuid().ToString("N"), now).Status == ResolutionStatus.MismatchedToken,
                "A stale editor token cannot authorize another playtest"
            );
            check(
                Restricted(profile) && Restricted(owner) && Restricted(normal),
                "Editor restrictions cover scratch, account and previous character identities"
            );
            check(!Restricted(SeasonRepository.NewId()), "Another account remains unrestricted");
            check(
                ScratchPath("user/profiles", profile + ".json") == Path.Combine(ScratchDirectory, profile + ".json"),
                "Native scratch save paths cannot target gameplay profile storage"
            );
            check(ScratchPath("user/profiles", normal + ".json") == null, "Gameplay save paths remain unchanged");
            Profiles.TryRemove(profile, out _);
            check(
                IsScratch(profile) && !Restricted(owner) && !Restricted(normal),
                "Retiring editor releases normal play while tombstones isolate late scratch saves"
            );
            check(
                Resolve(profile, session.Id, now).Status == ResolutionStatus.RetiredScratch,
                "A retired scratch identity cannot fall back to an owner or return profile"
            );
        }
        finally
        {
            Profiles.TryRemove(profile, out _);
            ScratchIds.TryRemove(profile, out _);
        }
    }
}
