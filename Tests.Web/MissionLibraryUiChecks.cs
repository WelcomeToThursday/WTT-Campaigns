using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Newtonsoft.Json;
using WTT.Campaigns.Server.Editor;
using WTT.Campaigns.Server.Seasons;
using WTT.Campaigns.Server.Web.Components;
using WTT.Campaigns.Shared.Authoring;
using WTT.Campaigns.Shared.Missions;
using WTT.Campaigns.Shared.Seasons;
using WTT.Campaigns.Shared.Spatial;

internal static class MissionLibraryUiChecks
{
    internal static async Task Run(IServiceProvider services, Action<bool, string> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "wtt-mission-documents-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "creator"));
        try
        {
            // A file-only repository fixture, never an SPT runtime or player profile.
            File.WriteAllText(Path.Combine(directory, "creator", "legacy.json"), JsonConvert.SerializeObject(new SeasonDefinition()));
            var repository = (SeasonRepository)Activator.CreateInstance(typeof(SeasonRepository), BindingFlags.Instance | BindingFlags.NonPublic,
                null, [directory], null)!;
            var draft = repository.CreateMission();
            var layout = draft.Definition.MapLayouts.Single();
            layout.Location = "woods";
            layout.Start = new() { Id = SeasonRepository.NewId(), Location = "woods", Scene = "woods_main" };
            layout.Checkpoints = [new() { Id = SeasonRepository.NewId(), Name = "Checkpoint", Location = "woods", Scene = "woods_main" }];
            layout.Exit = new() { Id = SeasonRepository.NewId(), Name = "Exit", Location = "woods", Scene = "woods_main" };
            draft = repository.Save(draft);
            var session = new EditorSessionRegistry.Session { Owner = "owner", Profile = "scratch", Draft = draft.Id,
                Layout = layout.Id, Location = "woods", Ready = true };
            var service = new EditorMissionTestService(repository, null!);
            var prepare = typeof(EditorMissionTestService).GetMethod("Prepare", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var before = JsonConvert.SerializeObject(repository.Load(draft.Id));
            var rehearsal = (EditorTestMissionResponse)prepare.Invoke(service, [session, new EditorTestMissionRequest
                { Version = 2, Action = EditorTestActions.Prepare, MissionId = draft.Definition.Missions.Single().Id }])!;
            check(rehearsal.Descriptor?.Definition.QuestId == "" && rehearsal.RunId.Length > 0,
                "Disposable mission preparation accepts an independent mission without a campaign quest");
            check(JsonConvert.SerializeObject(repository.Load(draft.Id)) == before, "Disposable preparation preserves its source mission draft");
            var pageType = typeof(WTT.Campaigns.Server.Web.Pages.Creator);
            var page = new WTT.Campaigns.Server.Web.Pages.Creator { MissionEditor = true, DraftQuery = draft.Id, SectionQuery = "Layouts" };
            const BindingFlags members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            pageType.GetProperty("Repository", members)!.SetValue(page, repository);
            var applyQuery = pageType.GetMethod("OnParametersSet", members)!;
            var currentDraft = pageType.GetField("_draft", members)!;
            var currentTab = pageType.GetField("_missionTab", members)!;
            applyQuery.Invoke(page, null);
            check(((DraftEnvelope?)currentDraft.GetValue(page))?.Id == draft.Id && (string?)currentTab.GetValue(page) == "Layouts",
                "Direct layout navigation loads the requested saved draft and selects Layouts");
            currentDraft.SetValue(page, null);
            applyQuery.Invoke(page, null);
            check(((DraftEnvelope?)currentDraft.GetValue(page))?.Id == draft.Id,
                "Returning to the library does not prevent reopening the same saved draft");
            var openedDraft = currentDraft.GetValue(page);
            page.SectionQuery = null;
            applyQuery.Invoke(page, null);
            check(ReferenceEquals(openedDraft, currentDraft.GetValue(page)) && (string?)currentTab.GetValue(page) == "Missions",
                "Switching mission workspace query preserves the open draft");
            currentDraft.SetValue(page, null);
            foreach (var search in new[] { "", "Interchange" })
            {
                pageType.GetField("_librarySearch", members)!.SetValue(page, search);
                using var pageTree = new RenderTreeBuilder();
                pageType.GetMethod("BuildRenderTree", members)!.Invoke(page, [pageTree]);
                check(ActualLibrarySearch(pageTree) == search,
                    "Actual Creator page passes the search box value to campaign layout discovery: " + search);
            }
            await using var renderer = new EditorRenderer(services);
            await renderer.Dispatcher.InvokeAsync(async () =>
            {
                await renderer.Mount(new Host(draft.Definition));
                var text = string.Join(" ", renderer.Components<EditorSection>().Select(c => renderer.Text(c.Id)));
                check(text.Contains("Mission layout") && !text.Contains("Story quest") && !text.Contains("Completion objective"),
                    "Independent mission editing offers layout and briefing without requiring quest fields");
                check(renderer.Components<MissionLogicFields>().Count() == 1, "Independent mission editor retains objective and event editing");
                var campaign = new DraftEnvelope { Id = "legacy-draft", Definition = new SeasonDefinition
                {
                    Name = "New campaign", Missions = [new() { Name = "Test" }], MapLayouts = [new() { Name = "Test layout", Location = "Interchange" }],
                } };
                await renderer.Mount(new LibraryHost([campaign, draft]));
                var legacyText = string.Join(" ", renderer.Components<CampaignDraftMissions>().Select(c => renderer.Text(c.Id)));
                check(legacyText.Contains("New campaign") && legacyText.Contains("Test layout") && legacyText.Contains("Edit missions and layouts"),
                    "Mission library discovers existing campaign missions by mission or layout name and offers editing");
                check(!legacyText.Contains("Mission layout"), "Campaign mission discovery excludes independent mission drafts");
                await renderer.Mount(new LibraryHost([campaign, draft], "Interchange"));
                var mapText = string.Join(" ", renderer.Components<CampaignDraftMissions>().Select(c => renderer.Text(c.Id)));
                check(mapText.Contains("Test layout") && mapText.Contains("Interchange") && mapText.Contains("Open layouts"),
                    "Existing layouts are searchable by map and offer direct layout access");
            });
            var target = typeof(SPTarkov.Server.Core.Controllers.QuestController).GetMethods().Single(m => m.Name == "CompleteQuest");
            check(target.GetParameters().Select(p => p.ParameterType).Take(3).SequenceEqual(new[]
            {
                typeof(SPTarkov.Server.Core.Models.Eft.Common.PmcData),
                typeof(SPTarkov.Server.Core.Models.Eft.Quests.CompleteQuestRequestData),
                typeof(SPTarkov.Server.Core.Models.Common.MongoId),
            }), "Quest-completed mission patch matches the installed native controller signature");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private sealed class LibraryHost(List<DraftEnvelope> drafts, string search = "Test") : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<CampaignDraftMissions>(0);
            builder.AddAttribute(1, "Drafts", drafts);
            builder.AddAttribute(2, "Search", search);
            builder.CloseComponent();
        }
    }

    private static string? ActualLibrarySearch(RenderTreeBuilder tree)
    {
        var frames = tree.GetFrames();
        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames.Array[i];
            if (frame.FrameType == Microsoft.AspNetCore.Components.RenderTree.RenderTreeFrameType.Component && frame.ComponentType == typeof(CampaignDraftMissions))
            {
                for (var j = i + 1; j < i + frame.ComponentSubtreeLength; j++)
                    if (frames.Array[j].FrameType == Microsoft.AspNetCore.Components.RenderTree.RenderTreeFrameType.Attribute && frames.Array[j].AttributeName == "Search")
                        return (string?)frames.Array[j].AttributeValue;
            }
            if (frame.FrameType == Microsoft.AspNetCore.Components.RenderTree.RenderTreeFrameType.Attribute && frame.AttributeValue is RenderFragment fragment)
            {
                using var child = new RenderTreeBuilder();
                fragment(child);
                var found = ActualLibrarySearch(child);
                if (found != null) return found;
            }
        }
        return null;
    }

    private sealed class Host(SeasonDefinition definition) : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<MissionWorkspace>(0);
            builder.AddAttribute(1, "Season", definition);
            builder.CloseComponent();
        }
    }
}
