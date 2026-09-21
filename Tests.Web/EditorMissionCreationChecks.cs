using System.Reflection;
using Newtonsoft.Json;
using WTT.Campaigns.Server.Editor;
using WTT.Campaigns.Server.Seasons;
using WTT.Campaigns.Shared.Authoring;
using WTT.Campaigns.Shared.Seasons;

namespace WTT.Campaigns.Web.Tests;

internal static class EditorMissionCreationChecks
{
    private sealed class Registry : EditorSessionRegistry
    {
        internal static void Add(Session session) => Profiles[session.Profile] = session;
        internal static void Remove(Session session) => Profiles.TryRemove(session.Profile, out _);
    }

    internal static void Run(Action<bool, string> check)
    {
        // File-only drafts and a synthetic registry entry: no SPT host or player profile.
        var folder = Path.Combine(Path.GetTempPath(), "wtt-mission-create-" + Guid.NewGuid().ToString("N"));
        var session = new EditorSessionRegistry.Session
        {
            Owner = Guid.NewGuid().ToString("N"),
            Profile = Guid.NewGuid().ToString("N"),
            Ready = true,
        };
        Directory.CreateDirectory(Path.Combine(folder, "creator"));
        try
        {
            File.WriteAllText(Path.Combine(folder, "creator", "legacy.json"), JsonConvert.SerializeObject(new SeasonDefinition()));
            var store = (SeasonRepository)Activator.CreateInstance(
                typeof(SeasonRepository), BindingFlags.Instance | BindingFlags.NonPublic, null, [folder], null
            )!;
            var existing = store.Save(new DraftEnvelope
            {
                Id = SeasonRepository.NewId(),
                Definition = new SeasonDefinition { Id = SeasonRepository.NewId(), Name = "Existing campaign" },
            });
            var before = JsonConvert.SerializeObject(store.Load(existing.Id));
            session.Select(existing.Id, "");
            Registry.Add(session);
            var editor = new EditorSessions(null!, null!, null!, null!, store, null!);
            var request = new EditorSessionRequest { SessionId = session.Id, Name = "  Factory rescue  ", Location = "factory4_day" };
            void Reject(string owner, EditorSessionRequest input, string reason)
            {
                var count = store.Drafts().Count;
                var selection = (session.Draft, session.Layout, session.Location);
                var rejected = false;
                try { editor.CreateMission(owner, input); }
                catch (InvalidOperationException) { rejected = true; }
                check(rejected && store.Drafts().Count == count && selection == (session.Draft, session.Layout, session.Location), reason);
            }
            Reject("wrong-owner", request, "Mission creation rejects another owner without writing or changing selection");
            request.SessionId = "wrong-token";
            Reject(session.Owner, request, "Mission creation rejects stale session tokens");
            request.SessionId = session.Id;
            session.Contact = DateTimeOffset.UtcNow.AddMinutes(-2);
            Reject(session.Owner, request, "Mission creation rejects expired sessions");
            session.Contact = DateTimeOffset.UtcNow;
            session.Ready = false;
            Reject(session.Owner, request, "Mission creation requires a ready editor session");
            session.Ready = true;
            foreach (var (name, location) in new[] { (" ", "woods"), (new string('x', 121), "woods"), ("Mission", ""), ("Mission", "hideout") })
                Reject(session.Owner, new() { SessionId = session.Id, Name = name, Location = location }, "Invalid mission input leaves no draft");

            var response = editor.CreateMission(session.Owner, request);
            var created = store.Load(response.DraftId);
            var layout = created.Definition.MapLayouts.Single();
            var mission = created.Definition.Missions.Single();
            check(response.Mode == EditorContentMode.Mission && response.LayoutId == layout.Id, "Creation selects the new mission layout");
            check(mission.Name == "Factory rescue" && layout.Name == mission.Name && created.Definition.Name == mission.Name,
                "Creation persists the trimmed mission name consistently");
            check(layout.Location == request.Location && mission.LayoutId == layout.Id, "Creation persists the chosen map and mission ownership");
            check(created.Definition.MissionPackage != null && response.Drafts.Any(d => d.Id == created.Id)
                && response.Layouts.Single().Location == request.Location, "New mission immediately appears in the home response");
            check(!layout.ApplyInNormalRaids && store.Packs().Count == 0, "Creation neither publishes nor enables ordinary-raid content");
            check(JsonConvert.SerializeObject(store.Load(existing.Id)) == before, "New Mission preserves the previously selected campaign");
            editor.Map(session.Owner, new() { SessionId = session.Id, Location = request.Location });
            check(session.Location == request.Location, "The selected new mission passes the actual editor map gate");
            Reject(session.Owner, request, "Mission creation while a map is open is rejected without leaving an orphan draft");
            var blank = store.CreateMission();
            check(blank.Definition.Name == "New mission" && blank.Definition.MapLayouts.Single().Location == "",
                "Existing Creator blank-mission defaults remain available");
        }
        finally
        {
            Registry.Remove(session);
            Directory.Delete(folder, true);
        }
    }
}
