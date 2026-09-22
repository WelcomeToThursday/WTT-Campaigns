using Newtonsoft.Json;
using SPTarkov.Server.Core.Models.Eft.Common;
using WTT.Campaigns.Server.Missions;
using WTT.Campaigns.Server.Seasons;
using WTT.Campaigns.Shared.Missions;
using WTT.Campaigns.Shared.Seasons;
using WTT.Campaigns.Shared.Story;

namespace WTT.Campaigns.Tests;

internal static class MissionLibraryChecks
{
    internal static void Run(SeasonRepository store, Action<bool, string> check)
    {
        var draft = store.CreateMission();
        var package = draft.Definition;
        package.MapLayouts = [MapEditorChecks.Example()];
        package.Missions[0].LayoutId = package.MapLayouts[0].Id;
        check(package.MissionPackage is { AllowStandalonePlay: false }, "Independent missions default to campaign-only access");
        var validation = SeasonValidator.Validate(package);
        check(
            validation.CanPublish,
            "Mission without a campaign or quest validates: " + string.Join("; ", validation.Issues.Select(i => i.Message))
        );
        package.MapLayouts[0].Navigation = new();
        package.FormatVersion = 13;
        validation = SeasonValidator.Validate(package);
        check(validation.CanPublish, "Independent mission package accepts a saved navigation recipe in format 13");
        var future = SeasonCompiler.Copy(package);
        future.FormatVersion = 15;
        check(!SeasonValidator.Validate(future).CanPublish, "Independent mission package rejects unknown future formats");
        draft = store.Save(draft);
        var key = store.Publish(draft, validation);
        var published = store.Pack(key);
        check(
            published.FormatVersion == 13 && published.MapLayouts[0].Navigation != null,
            "Publication retains navigation recipes and their required format"
        );
        check(published.Revision == 1 && published.MissionPackage != null, "Mission publication creates a versioned package");
        var campaign = store.Create(false);
        var image = store.AddImage(
            Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==")
        );
        campaign.Definition.UniversalImage = campaign.Definition.UniversalUnavailableImage = image;
        campaign.Definition.Documents[0].Image = campaign.Definition.Documents[0].UnavailableImage = image;
        var link = store.LinkMission(campaign.Definition, key);
        check(
            link.Availability == MissionAvailability.FromStart && link.Revision == 1,
            "Campaign links start available and pin a published revision"
        );
        var untouched = JsonConvert.SerializeObject(published);
        package.Missions[0].CheckpointRetries = false;
        draft = store.Save(draft);
        var secondKey = store.Publish(draft, SeasonValidator.Validate(package));
        check(
            link.Revision == 1 && JsonConvert.SerializeObject(link.Package) == untouched,
            "Publishing a new mission revision does not change a campaign link"
        );
        store.UpdateMissionLink(link, secondKey);
        check(link.Revision == 2, "Authors can explicitly update a linked revision");
        var duplicateRejected = false;
        try
        {
            store.LinkMission(campaign.Definition, secondKey);
        }
        catch (InvalidOperationException)
        {
            duplicateRejected = true;
        }
        check(duplicateRejected, "A campaign cannot link the same mission twice");
        var resolved = MissionLibrary.Resolve(campaign.Definition);
        var resolvedMission = resolved.Missions.Single();
        check(
            resolvedMission.Id == link.Id && resolved.MapLayouts.Any(l => l.Id == resolvedMission.LayoutId),
            "Runtime composition preserves link identity and resolves its own layout"
        );
        check(resolvedMission.LayoutId != link.Package.MapLayouts[0].Id, "Linked layout identities are scoped to their campaign link");
        check(
            JsonConvert.SerializeObject(MissionLibrary.Resolve(campaign.Definition)) == JsonConvert.SerializeObject(resolved),
            "Runtime layout namespacing is deterministic"
        );
        check(
            MissionLibrary.Standalone([published], null).Missions.Count == 0,
            "Campaign-only publication is absent from the normal character library"
        );
        var standalone = SeasonCompiler.Copy(published);
        standalone.MissionPackage!.AllowStandalonePlay = true;
        var newer = SeasonCompiler.Copy(standalone);
        newer.Revision = 2;
        var standaloneRun = new MissionRun
        {
            ContextVersion = 3,
            Scope = MissionLibrary.StandaloneScope,
            PackageId = standalone.Id,
            PackageRevision = 1,
            MissionId = standalone.Missions[0].Id,
            Status = MissionRunStatuses.Active,
        };
        check(
            MissionLibrary.Standalone([standalone, newer], null).MissionLinks.Single().Revision == 2,
            "Normal character library selects the latest published mission revision"
        );
        check(
            MissionLibrary.Standalone([standalone, newer], standaloneRun).MissionLinks.Single().Revision == 1,
            "An active standalone run retains its prepared mission revision"
        );
        newer.MissionPackage!.AllowStandalonePlay = false;
        check(
            MissionLibrary.Standalone([standalone, newer], null).Missions.Count == 0,
            "A latest revision disabling standalone access does not expose older revisions"
        );
        check(
            MissionLibrary.Standalone([standalone, newer], standaloneRun).MissionLinks.Single().Revision == 1,
            "An already active run remains bound to its original available revision"
        );
        standaloneRun.Scope = "another-campaign";
        check(
            MissionLibrary.Standalone([standalone, newer], standaloneRun).Missions.Count == 0,
            "A campaign run cannot authorize an old standalone revision"
        );
        var copied = SeasonRepository.Duplicate(campaign.Definition);
        check(
            copied.MissionLinks[0].Id != link.Id
                && copied.MissionLinks[0].ContentHash == link.ContentHash
                && JsonConvert.SerializeObject(copied.MissionLinks[0].Package) == JsonConvert.SerializeObject(link.Package),
            "Campaign duplication remaps link identity without rewriting pinned packages"
        );
        var invalid = SeasonCompiler.Copy(campaign.Definition);
        invalid.MissionLinks[0].Package.Missions[0].CheckpointRetries = !invalid.MissionLinks[0].Package.Missions[0].CheckpointRetries;
        check(
            SeasonValidator.Validate(invalid).Issues.Any(i => i.Message.Contains("checksum")),
            "Edited pinned mission content fails checksum validation"
        );
        campaign = store.Save(campaign);
        var campaignValidation = SeasonValidator.Validate(campaign.Definition);
        check(
            campaignValidation.CanPublish,
            "Campaign with mission link validates: " + string.Join("; ", campaignValidation.Issues.Select(i => i.Message))
        );
        var campaignKey = store.Publish(campaign, campaignValidation);
        var imported = store.Import(store.Export(campaignKey));
        check(
            imported.Definition.MissionLinks[0].Package.Missions.Count == 1 && imported.Definition.MissionLinks[0].Revision == 2,
            "Campaign export/import carries the pinned mission package"
        );
        check(
            !imported.Definition.MissionLinks[0].Package.MissionPackage!.AllowStandalonePlay,
            "Campaign sharing does not grant standalone access"
        );
        var used = false;
        store.MarkUsed(campaign.Definition);
        campaign.Definition.MissionLinks[0].Availability = MissionAvailability.StoryAction;
        try
        {
            store.CheckGameplay(campaign.Definition);
        }
        catch (InvalidOperationException)
        {
            used = true;
        }
        check(!used, "Used campaigns can update mission availability");

        var quest = SeasonRepository.NewId();
        var chapter = SeasonRepository.NewId();
        var variable = SeasonRepository.NewId();
        var story = new StoryDefinition
        {
            Chapters =
            [
                new()
                {
                    Id = chapter,
                    Visibility = new()
                    {
                        Type = "VariableValue",
                        Target = variable,
                        Value = 1,
                    },
                },
            ],
            Variables = [new() { Id = variable, Scope = StoryVariableScope.Profile }],
        };
        var progress = new StoryProgress();
        var facts = new StoryFacts();
        link.Availability = MissionAvailability.FromStart;
        check(MissionLibrary.Eligible(link, null, progress, facts), "From-start links need no story or quest");
        link.Availability = MissionAvailability.QuestAccepted;
        link.UnlockTargetId = quest;
        foreach (var status in new[] { "Locked", "AvailableForStart", "Fail", "Started", "AvailableForFinish", "Success" })
        {
            facts.QuestStatuses[quest] = status;
            check(
                MissionLibrary.Eligible(link, story, progress, facts) == (status is "Started" or "AvailableForFinish" or "Success"),
                "Accepted gate: " + status
            );
            link.Availability = MissionAvailability.QuestCompleted;
            check(MissionLibrary.Eligible(link, story, progress, facts) == (status == "Success"), "Completed gate: " + status);
            link.Availability = MissionAvailability.QuestAccepted;
        }
        link.Availability = MissionAvailability.ChapterReached;
        link.UnlockTargetId = chapter;
        check(!MissionLibrary.Eligible(link, story, progress, facts), "Chapter missions remain locked before chapter visibility");
        progress.Variables[variable] = 1;
        check(MissionLibrary.Eligible(link, story, progress, facts), "Chapter visibility unlocks a linked mission");
        link.Availability = MissionAvailability.StoryAction;
        check(!MissionLibrary.Eligible(link, story, progress, facts), "Story-action missions start locked");
        var engine = new StoryEngine(story, progress, facts, _ => throw new Exception("Unexpected native action"), _ => 0, 1);
        engine.Apply([new() { Type = StoryActionType.UnlockMission, Target = link.Id }]);
        check(MissionLibrary.Eligible(link, story, progress, facts), "Unlock mission story action grants the specified link");
        check(!MissionLibrary.Eligible(link, story, new StoryProgress(), facts), "Story unlocks cannot cross character progress");
        var pmc = new PmcData { ExtensionData = new() };
        MissionStore.Write(
            pmc,
            new MissionProgress
            {
                SeasonId = "campaign",
                CompletedMissionIds = [link.Id],
                UnlockedMissionIds = [link.Id],
            }
        );
        check(
            MissionStore.Read(pmc, "campaign").CompletedMissionIds.Contains(link.Id),
            "Early completion survives a profile storage round trip"
        );
        check(
            MissionStore.Read(pmc, MissionLibrary.StandaloneScope).CompletedMissionIds.Count == 0
                && MissionStore.Read(pmc, "other-campaign").CompletedMissionIds.Count == 0,
            "Standalone and campaign completion namespaces remain isolated"
        );
        var completionId = SeasonRepository.NewId();
        var completionDefinition = new SeasonDefinition
        {
            Id = "campaign",
            Story = story,
            Quests =
            [
                new()
                {
                    Id = quest,
                    Conditions = new()
                    {
                        AvailableForFinish =
                        [
                            new()
                            {
                                Id = completionId,
                                ConditionType = "GlobalVariableValue",
                                Target = variable,
                                Value = 1,
                                CompareMethod = ">=",
                            },
                        ],
                    },
                },
            ],
        };
        story.Quests.Add(new() { QuestId = quest });
        var completionMission = new MissionDefinition { QuestId = quest, CompletionConditionId = completionId };
        MissionCompletion.Apply(pmc, completionDefinition, completionMission);
        check(
            !pmc.ExtensionData.ContainsKey("wttCampaignsStory:campaign"),
            "Early mission completion does not auto-accept or advance an absent quest"
        );
        pmc.Quests =
        [
            new()
            {
                StartTime = 1,
                StatusTimers = new(),
                QId = new SPTarkov.Server.Core.Models.Common.MongoId(quest),
                Status = SPTarkov.Server.Core.Models.Enums.QuestStatusEnum.Started,
            },
        ];
        MissionCompletion.Apply(pmc, completionDefinition, completionMission);
        check(
            pmc.Quests[0].CompletedConditions!.Contains(completionId)
                && pmc.Quests[0].Status == SPTarkov.Server.Core.Models.Enums.QuestStatusEnum.Started,
            "Previously completed mission satisfies an accepted objective without handing in the quest"
        );
        var afterCredit = JsonConvert.SerializeObject(pmc);
        MissionCompletion.Apply(pmc, completionDefinition, completionMission);
        check(JsonConvert.SerializeObject(pmc) == afterCredit, "Replaying mission completion does not duplicate quest credit or rewards");
        var acknowledged = new MissionRun
        {
            ContextVersion = 3,
            Scope = "campaign",
            PackageId = published.Id,
            PackageRevision = 1,
            RunId = "run",
            RaidId = "raid",
            CharacterId = "character",
            MissionId = link.Id,
            LayoutId = "layout",
            ContentHash = "hash",
            Status = MissionRunStatuses.Active,
        };
        var wrongScope = SeasonCompiler.Copy(acknowledged);
        wrongScope.Scope = MissionLibrary.StandaloneScope;
        var contextRejected = false;
        try
        {
            MissionAcknowledgement.Require(acknowledged, wrongScope, true);
        }
        catch (InvalidOperationException)
        {
            contextRejected = true;
        }
        check(contextRejected, "An acknowledgement from another mission context cannot release observations");
        var source = store.Create(false);
        source.Definition.MapLayouts = [MapEditorChecks.Example()];
        source.Definition.Missions =
        [
            new()
            {
                Id = SeasonRepository.NewId(),
                Name = "Extract me",
                Briefing = "Extract safely.",
                LayoutId = source.Definition.MapLayouts[0].Id,
                QuestId = quest,
            },
        ];
        var before = JsonConvert.SerializeObject(source);
        var extracted = store.ExtractMission(source, source.Definition.Missions[0].Id);
        check(JsonConvert.SerializeObject(source) == before, "Interrupted extraction leaves source mission, layout and draft untouched");
        check(
            extracted.Definition.Missions[0].QuestId == "" && extracted.Definition.Missions[0].Id == source.Definition.Missions[0].Id,
            "Extraction separates quest bindings and retains a migration identity"
        );
    }
}
