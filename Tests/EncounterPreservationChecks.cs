using WTT.Campaigns.Server.Seasons;
using WTT.Campaigns.Server.Web.Authoring;
using WTT.Campaigns.Shared.Authoring;
using WTT.Campaigns.Shared.Seasons;
using WTT.Campaigns.Shared.Spatial;

namespace WTT.Campaigns.Tests;

internal sealed class EncounterPreservationChecks : WTT.Campaigns.Server.Editor.EditorSessionRegistry
{
    internal static void Run(SeasonRepository repository, Action<bool, string> check)
    {
        var draft = repository.Create(false);
        draft.Definition.FormatVersion = 6;
        var layout = new MapLayout
        {
            Id = SeasonRepository.NewId(),
            Name = "AI preservation",
            Location = "woods",
        };
        layout.Encounters.Add(
            new MapEncounter
            {
                Id = SeasonRepository.NewId(),
                Name = "Entry encounter",
                Trigger = new MapEncounterTrigger { Type = MapEncounterTrigger.MissionStart },
            }
        );
        draft.Definition.MapLayouts.Add(layout);
        draft = repository.Save(draft);
        var pack = repository.Publish(draft, SeasonValidator.Validate(draft.Definition));
        var imported = repository.Import(repository.Export(pack));
        check(
            imported.Definition.FormatVersion == 6 && imported.Definition.MapLayouts[0].Encounters.Count == 1,
            "Format 6 pack export/import preserves layout encounters"
        );
        // AI editing belongs to a mission, including legacy embedded missions.
        draft.Definition.Missions.Add(new() { Id = SeasonRepository.NewId(), LayoutId = layout.Id, Name = "AI mission" });
        draft = repository.Save(draft);
        var service = new RaidAuthoringService(repository);
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
            foreach (var version in new[] { 2, 3, 4 })
            {
                var request = new AuthoringRequest
                {
                    Version = version,
                    ClientId = Guid.NewGuid().ToString("N"),
                    RaidId = Guid.NewGuid().ToString("N"),
                    Location = "woods",
                    Enabled = true,
                    EditorSessionId = session.Id,
                };
                service.Poll(owner, profile, request);
                service.Connect(request.ClientId, draft.Id);
                var response = service.Poll(owner, profile, request);
                request.Grant = response.Grant;
                request.DraftId = response.DraftId;
                request.Revision = response.Revision;
                request.Definition = SeasonCompiler.Copy(response.Definition!);
                request.Definition.MapLayouts[0].Encounters.Clear();
                request.OperationId = Guid.NewGuid().ToString("N");
                var rejected = false;
                try
                {
                    service.Submit(owner, profile, request);
                }
                catch (InvalidOperationException)
                {
                    rejected = true;
                }
                check(rejected == (version < 4), "Only AI-aware clients can intentionally remove AI records: protocol " + version);
                check(
                    repository.Load(draft.Id).Definition.MapLayouts[0].Encounters.Count == (version < 4 ? 1 : 0),
                    "Older submissions preserve stored encounters: protocol " + version
                );
            }
        }
        finally
        {
            Profiles.TryRemove(profile, out _);
        }
    }
}
