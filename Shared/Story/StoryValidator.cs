using WTT.Campaigns.Shared.Native;
using WTT.Campaigns.Shared.Seasons;
using WTT.Campaigns.Shared.Serialization;

namespace WTT.Campaigns.Shared.Story;

public static class StoryValidator
{
    public static void Validate(SeasonDefinition season, SeasonValidationResult result)
    {
        if (season.Story == null)
        {
            return;
        }
        var story = season.Story;
        void Need(bool valid, string path, string message)
        {
            if (!valid)
            {
                result.Add("Story/" + path, message);
            }
        }
        Need(story.FormatVersion == 1, "", "Unsupported story format version.");
        if (
            story.Chapters.Count > 100
            || story.Quests.Count > 1000
            || story.Dialogs.Count > 1000
            || story.Dialogs.Sum(d => d.Lines.Count) > 20000
            || story.Notes.Count > 10000
            || story.Variables.Count > 20000
            || story.RaidBindings.Count > 10000
        )
        {
            result.Add("Story", "Story exceeds pack limits.");
            return;
        }
        var owned = new HashSet<string> { season.Id, season.BattlePassId };
        owned.UnionWith(season.Documents.Select(d => d.Id));
        owned.UnionWith(season.Items.Select(i => i.Id));
        owned.UnionWith(season.ImportedItems.Keys);
        owned.UnionWith(season.Perks.All.Select(p => p.Id));
        owned.UnionWith(season.AllRewards.Select(r => r.Id));
        owned.UnionWith(
            season
                .Quests.Select(q => q.Id)
                .Concat(season.Quests.SelectMany(q => q.AllConditions()).Select(c => c.Id))
                .Concat(season.Quests.SelectMany(q => q.AllItems()).Select(i => i.Id))
        );
        var ids = new HashSet<string>();
        void Identity(string id, string path)
        {
            Need(SeasonValidator.IsId(id), path, "A valid 24-character identity is required.");
            Need(ids.Add(id), path, "Duplicate story identity: " + id);
            Need(!owned.Contains(id), path, "Story identity collides with another owned campaign object: " + id);
        }
        var chapters = story.Chapters.Select(x => x.Id).ToHashSet();
        var dialogs = story.Dialogs.Select(x => x.Id).ToHashSet();
        var notes = story.Notes.Select(x => x.Id).ToHashSet();
        var variables = story.Variables.Select(x => x.Id).ToHashSet();
        var media = story.Media.Select(x => x.Id).ToHashSet();
        var entries = story.EntryPoints.Select(x => x.Id).ToHashSet();
        var bindings = story.RaidBindings.Select(x => x.Id).ToHashSet();
        var quests = season
            .Quests.Select(x => x.Id ?? "")
            .Concat(season.Dependencies.Where(x => x.StartsWith("quest:", StringComparison.Ordinal)).Select(x => x.Substring(6)))
            .ToHashSet();
        var conditions = season.Quests.SelectMany(q => q.AllConditions()).Select(c => (string?)c.Id ?? "").ToHashSet();
        void Condition(StoryCondition c, string path, int depth = 0)
        {
            if (depth > 32)
            {
                Need(false, path, "Conditions may nest at most 32 levels.");
                return;
            }
            Need(StoryRules.ConditionTypes.Contains(c.Type), path, "Unsupported condition: " + c.Type);
            Need(c.Type != "ServiceAvailable", path, "Paid dialogue services have no beta adapter. Use the native trader Services screen.");
            Need(StoryRules.Operators.Contains(c.Operator), path, "Unsupported comparison: " + c.Operator);
            Need(!double.IsNaN(c.Value) && !double.IsInfinity(c.Value), path, "A finite condition value is required.");
            if (c.Type is "All" or "Any" or "Not")
            {
                Need(c.Type != "Not" || c.Conditions.Count == 1, path, "Not requires exactly one child condition.");
                foreach (var child in c.Conditions)
                {
                    Condition(child, path, depth + 1);
                }
                return;
            }
            Need(c.Conditions.Count == 0, path, "Only logical groups may contain child conditions.");
            Need(c.Type is "Level" or "HasFreeSpecialSlot" || c.Target.Length > 0, path, "A condition target is required.");
            if (c.Type == "VariableValue")
            {
                Need(variables.Contains(c.Target), path, "Unknown variable: " + c.Target);
            }
            if (c.Type == "QuestStatus")
            {
                Need(quests.Contains(c.Target), path, "Unknown quest: " + c.Target);
                Need(c.Status.Count > 0 && c.Status.All(StoryRules.QuestStatuses.Contains), path, "Named quest statuses are required.");
            }
            if (c.Type is "QuestConditionStatus" or "CompleteCondition" or "HasItemForHandover")
            {
                Need(conditions.Contains(c.Target), path, "Unknown objective: " + c.Target);
            }
            if (c.Type == "LocationTrigger")
            {
                Need(bindings.Contains(c.Target), path, "Unknown raid binding: " + c.Target);
            }
        }
        void Action(StoryAction a, string path)
        {
            Identity(a.Id, path);
            Need(Enum.IsDefined(typeof(StoryActionType), a.Type), path, "Unsupported dialogue action.");
            Need(
                a.Type != StoryActionType.PurchaseService,
                path,
                "Paid dialogue services have no beta adapter. Use SelectSubService to open native Services."
            );
            if (a.Type == StoryActionType.UnlockMission)
                Need(
                    season.MissionLinks.Any(l => l.Id == a.Target && l.Availability == Missions.MissionAvailability.StoryAction),
                    path,
                    "Choose a mission link unlocked by story action."
                );
            if (a.Type == StoryActionType.SetVariable)
            {
                var variable = story.Variables.FirstOrDefault(v => v.Id == a.Target);
                Need(variable != null && variable.Scope == a.Scope, path, "Variable target and scope must match its declaration.");
            }
            if (a.Type == StoryActionType.DiaryNote)
            {
                Need(notes.Contains(a.Target), path, "Unknown note: " + a.Target);
            }
            if (a.Type == StoryActionType.TraderStanding)
            {
                Need(SeasonValidator.IsId(a.Target), path, "A valid trader target is required.");
                Need(
                    !double.IsNaN(a.StandingChange) && !double.IsInfinity(a.StandingChange),
                    path,
                    "Trader standing change must be finite."
                );
            }
            if (a.Type == StoryActionType.CompleteItem)
            {
                Need(SeasonValidator.IsId(a.Target), path, "A valid 24-character completion target is required.");
            }

            if (a.Type is StoryActionType.SwitchDialog or StoryActionType.EmbedQuestDialog)
            {
                Need(dialogs.Contains(a.Target), path, "Unknown dialogue: " + a.Target);
            }
            if (
                a.Type
                is StoryActionType.AcceptQuest
                    or StoryActionType.FinishQuest
                    or StoryActionType.FailQuest
                    or StoryActionType.HandoverItem
                    or StoryActionType.SelectQuest
                    or StoryActionType.PlayerReward
            )
            {
                Need(
                    a.QuestId.Length == 0 && a.Type != StoryActionType.SelectQuest || quests.Contains(a.QuestId),
                    path,
                    "Unknown quest action target: " + a.QuestId
                );
                if (a.Type != StoryActionType.SelectQuest)
                {
                    Need(
                        a.QuestId.Length == 0 || story.Quests.Any(q => q.QuestId == a.QuestId) && season.Quests.Any(q => q.Id == a.QuestId),
                        path,
                        "Native mutations require a quest owned by this story."
                    );
                }
            }
            if (a.Type == StoryActionType.HandoverItem)
            {
                Need(conditions.Contains(a.ConditionId), path, "Unknown handover objective: " + a.ConditionId);
            }
            if (a.Type == StoryActionType.StartCinematic)
            {
                Need(
                    story.Media.Any(m => m.Id == a.Target && m.Kind is "Cinematic" or "Video"),
                    path,
                    "A cinematic or video media target is required."
                );
            }
        }
        foreach (var chapter in story.Chapters)
        {
            Identity(chapter.Id, chapter.Id);
            Need(chapter.Name.Length > 0, chapter.Id, "Chapter name is required.");
            Need(string.IsNullOrEmpty(chapter.Image) || SeasonValidator.IsId(chapter.Image), chapter.Id, "Invalid chapter image.");
            Need(string.IsNullOrEmpty(chapter.Icon) || SeasonValidator.IsId(chapter.Icon), chapter.Id, "Invalid chapter icon.");
            Condition(chapter.Visibility, chapter.Id);
        }
        var memberships = new HashSet<string>();
        foreach (var quest in story.Quests)
        {
            Need(memberships.Add(quest.QuestId), quest.QuestId, "A quest may belong to only one chapter.");
            Need(quests.Contains(quest.QuestId), quest.QuestId, "Story quest needs a native definition or declared quest dependency.");
            Need(chapters.Contains(quest.ChapterId), quest.QuestId, "Unknown chapter.");
            Condition(quest.Visibility, quest.QuestId);
            foreach (var pair in quest.StatusNotes)
            {
                Need(StoryRules.QuestStatuses.Contains(pair.Key), quest.QuestId, "Unknown note-trigger status.");
                Need(pair.Value.All(notes.Contains), quest.QuestId, "Unknown status note.");
            }
        }
        foreach (var note in story.Notes)
        {
            Identity(note.Id, note.Id);
            Need(chapters.Contains(note.ChapterId), note.Id, "Unknown note chapter.");
            Need(note.Text.Length > 0, note.Id, "Note text is required.");
            Need(note.ConditionIds.All(conditions.Contains), note.Id, "Unknown note objective.");
            foreach (var link in note.Links)
            {
                Identity(link.Id, note.Id);
                Need(link.Kind is "Item" or "Offer" or "Craft", note.Id, "Unsupported journal link kind.");
                Need(SeasonValidator.IsId(link.Target), note.Id, "Invalid journal link target.");
                Need(link.Kind != "Offer" || SeasonValidator.IsId(link.TraderId), note.Id, "Offer links require a trader.");
            }
        }
        foreach (var variable in story.Variables)
        {
            Identity(variable.Id, variable.Id);
            Need(Enum.IsDefined(typeof(StoryVariableScope), variable.Scope), variable.Id, "Unsupported variable scope.");
        }
        foreach (var dialog in story.Dialogs)
        {
            Identity(dialog.Id, dialog.Id);
            Need(SeasonValidator.IsId(dialog.TraderId), dialog.Id, "A trader identity is required.");
            Need(variables.Contains(dialog.MainVariable), dialog.Id, "Declare the dialogue's main variable.");
            Need(dialog.Lines.Count > 0, dialog.Id, "A dialogue requires lines.");
            Need(dialog.StartPoints.Keys.All(k => k.Length is > 0 and <= 64), dialog.Id, "Start points require names up to 64 characters.");
            foreach (
                var group in dialog.Lines.Where(l => l.Random != null).Select(l => l.Random!).GroupBy(r => r.VariableId + ":" + r.Group)
            )
            {
                Need(group.Select(r => r.Maximum).Distinct().Count() == 1, dialog.Id, "A random group must use the same maximum.");
            }
            foreach (var line in dialog.Lines)
            {
                Identity(line.Id, dialog.Id);
                Need(line.Side is "Npc" or "Player", line.Id, "Dialogue side must be Npc or Player.");
                Need(line.Text.Length > 0, line.Id, "Dialogue line text is required.");
                Condition(line.Trigger, line.Id);
                foreach (var action in line.Actions)
                {
                    Action(action, line.Id);
                }
                foreach (
                    var reference in new[]
                    {
                        (line.Playback.Image, "Image"),
                        (line.Playback.Music, "Audio"),
                        (line.Playback.Sound, "Audio"),
                    }
                )
                {
                    Need(
                        reference.Item1.Length == 0 || story.Media.Any(m => m.Id == reference.Item1 && m.Kind == reference.Item2),
                        line.Id,
                        "Playback media is missing or has the wrong kind."
                    );
                }
                foreach (
                    var sequence in line
                        .Playback.Animations.Concat(line.Playback.SecondaryAnimations)
                        .Concat(line.Playback.LipSyncs)
                        .Concat(line.Playback.Subtitles)
                )
                {
                    Need(
                        sequence.Key.Length > 0
                            && sequence.Start >= 0
                            && sequence.End >= sequence.Start
                            && sequence.End <= 3600
                            && sequence.Speed > 0
                            && sequence.Speed <= 10
                            && sequence.Volume >= 0
                            && sequence.Volume <= 1,
                        line.Id,
                        "Invalid playback timing or media key."
                    );
                }
                if (line.Random is { } random)
                {
                    Need(
                        random.VariableId.Length > 0
                            && random.Maximum > 0
                            && random.Start >= 0
                            && random.End >= random.Start
                            && random.End < random.Maximum,
                        line.Id,
                        "Invalid random selection range."
                    );
                }
            }
        }
        foreach (var entry in story.EntryPoints)
        {
            Identity(entry.Id, entry.Id);
            Need(dialogs.Contains(entry.DialogId), entry.Id, "Unknown entry dialogue.");
            Need(SeasonValidator.IsId(entry.TraderId), entry.Id, "Invalid entry trader.");
            var entryDialog = story.Dialogs.FirstOrDefault(d => d.Id == entry.DialogId);
            Need(entryDialog?.TraderId == entry.TraderId, entry.Id, "Entry and dialog must belong to the same trader.");
            Need(
                entry.StartPoint.Length == 0 || entryDialog?.StartPoints.ContainsKey(entry.StartPoint) == true,
                entry.Id,
                "Unknown start point."
            );
            Need(entry.Kind is "InLobby" or "InRaid" or "ViaRadio" or "ViaNotebook" or "ViaIntercom", entry.Id, "Unsupported entry point.");
            Condition(entry.Condition, entry.Id);
            Need(
                entry.Scene.Length <= 256 && !entry.Scene.Contains(":/") && !entry.Scene.Contains("\\"),
                entry.Id,
                "Use the exact Unity scene name, not an object path."
            );
        }
        foreach (var binding in story.RaidBindings)
        {
            Identity(binding.Id, binding.Id);
            Need(binding.Location.Length > 0, binding.Id, "A raid location is required.");
            Need(
                binding.Kind is "Trigger" or "Interact" or "Shoot" or "Collectible" or "Cinematic",
                binding.Id,
                "Unsupported raid binding."
            );
            Need(
                binding.Kind == "Collectible"
                    ? SeasonValidator.IsId(binding.ItemId)
                    : (binding.ObjectPath.Length > 0 || season.Zones.Any(z => z.Id == binding.ZoneId)),
                binding.Id,
                "A collectible item or exact scene object path is required."
            );
            Need(binding.EntryPointId.Length == 0 || entries.Contains(binding.EntryPointId), binding.Id, "Unknown bound entry point.");
            Need(binding.MediaId.Length == 0 || media.Contains(binding.MediaId), binding.Id, "Unknown bound media.");
            Need(
                binding.MediaId.Length == 0 || story.Media.Any(m => m.Id == binding.MediaId && m.Kind != "TraderScene"),
                binding.Id,
                "A trader room cannot be played as event media."
            );
            var boundEntry = story.EntryPoints.FirstOrDefault(e => e.Id == binding.EntryPointId);
            Need(boundEntry == null || boundEntry.Kind != "InLobby", binding.Id, "Raid events require a raid conversation entry.");
            if (boundEntry?.Scene.Length > 0 && binding.Kind != "Collectible")
            {
                Need(
                    (
                        binding.ZoneId.Length > 0
                            ? season.Zones.Any(z => z.Id == binding.ZoneId && z.Scene == boundEntry.Scene)
                            : binding.ObjectPath.StartsWith(boundEntry.Scene + ":/", StringComparison.Ordinal)
                    ),
                    binding.Id,
                    "The bound entry's scene must match the object path."
                );
            }

            Need(
                binding.Kind != "Cinematic" || story.Media.Any(m => m.Id == binding.MediaId && m.Kind is "Cinematic" or "Video"),
                binding.Id,
                "Cinematic bindings require a cinematic or video resource."
            );
            Condition(binding.Condition, binding.Id);
            foreach (var action in binding.Actions)
            {
                Action(action, binding.Id);
            }
        }
        foreach (var resource in story.Media)
        {
            Identity(resource.Id, resource.Id);
            Need(
                resource.Sha256.Length == 64 && resource.Sha256.All(Uri.IsHexDigit),
                resource.Id,
                "Media requires its bundle SHA-256 checksum."
            );
            Need(resource.Kind is "Image" or "Audio" or "Video" or "Cinematic" or "TraderScene", resource.Id, "Unsupported media kind.");
            Need(
                resource.TraderId.Length == 0 || resource.Kind == "TraderScene" && SeasonValidator.IsId(resource.TraderId),
                resource.Id,
                "Only trader rooms may be assigned to a valid trader."
            );
            Need(
                resource.TraderId.Length == 0 || story.Media.Count(m => m.Kind == "TraderScene" && m.TraderId == resource.TraderId) == 1,
                resource.Id,
                "Assign only one custom room to each trader."
            );
            if (resource.Kind == "TraderScene" && resource.TraderId.Length == 0)
            {
                result.Add("Story/" + resource.Id, "This trader room is unassigned and will not replace a visit room.", "warning");
            }

            Need(
                resource.Asset.Length > 0
                    && resource.Bundle.EndsWith(".bundle", StringComparison.OrdinalIgnoreCase)
                    && !resource.Asset.Contains("..")
                    && !resource.Asset.Contains(":")
                    && !resource.Bundle.Contains("..")
                    && !resource.Bundle.Contains(":")
                    && !resource.Bundle.StartsWith("/")
                    && !resource.Bundle.StartsWith("\\"),
                resource.Id,
                "Media must reference a local packaged asset."
            );
        }
    }
}
