using System.Collections;
using System.Reflection;
using Newtonsoft.Json;
using WTT.Campaigns.Shared.Seasons;
using WTT.Campaigns.Shared.Story;

namespace WTT.Campaigns.Server.Web.Authoring;

// Editor-only operations. Authoring metadata and selection never enter the pack format.
public static class StoryAuthoring
{
    public static string NewId()
    {
        return Guid.NewGuid().ToString("N")[..24];
    }

    public static string Id(object value)
    {
        return value.GetType().GetProperty(value is StoryQuest ? "QuestId" : "Id")?.GetValue(value)?.ToString() ?? "";
    }

    public static string Label(object value)
    {
        return value switch
        {
            StoryChapter c => c.Name,
            StoryNote n => n.Text,
            StoryDialog d => d.Lines.FirstOrDefault()?.Text ?? "New conversation",
            StoryDialogLine l => l.Side + ": " + l.Text,
            StoryVariable v => $"{v.Scope} variable · initial {v.InitialValue}",
            StoryEntryPoint e => $"{e.Kind} · {e.StartPoint}",
            StoryRaidBinding b => $"{b.Name} {b.Kind} · {b.Location} · {b.ObjectPath}",
            StoryMedia m => $"{m.Kind} · {m.Asset}",
            StoryAction a => Friendly(a.Type.ToString()),
            _ => value.GetType().Name.Replace("Story", ""),
        };
    }

    public static string Friendly(string text)
    {
        return Shared.Presentation.CampaignText.Display(System.Text.RegularExpressions.Regex.Replace(text, "([a-z])([A-Z])", "$1 $2"));
    }

    public static IEnumerable<object> Records(StoryDefinition? story)
    {
        return story == null
            ? []
            : story
                .Chapters.Cast<object>()
                .Concat(story.Quests)
                .Concat(story.Notes)
                .Concat(story.Dialogs)
                .Concat(story.Variables)
                .Concat(story.EntryPoints)
                .Concat(story.RaidBindings)
                .Concat(story.Media);
    }

    public static IEnumerable<(string Id, string Name)> Choices(SeasonDefinition season, string kind)
    {
        var story = season.Story ?? new();
        return kind switch
        {
            "chapters" => story.Chapters.Select(v => (v.Id, Label(v))),
            "notes" => story.Notes.Select(v => (v.Id, Label(v))),
            "dialogs" => story.Dialogs.Select(v => (v.Id, Label(v))),
            "missionLinks" => season.MissionLinks.Select(l => (l.Id, l.Package.Name)),
            "variables" => story.Variables.Select(v => (v.Id, ReferenceNames.Label(season, v, (_, id) => id))),
            "entries" => story.EntryPoints.Select(v => (v.Id, ReferenceNames.Label(season, v, (_, id) => id))),
            "bindings" => story.RaidBindings.Select(v => (v.Id, Label(v))),
            "media" or "eventmedia" or "Image" or "Audio" or "cinematic" => story
                .Media.Where(v =>
                    kind == "media"
                    || (
                        kind == "eventmedia" ? v.Kind != "TraderScene"
                        : kind == "cinematic" ? v.Kind is "Cinematic" or "Video"
                        : v.Kind == kind
                    )
                )
                .Select(v => (v.Id, Label(v))),
            "ownedquests" => season
                .Quests.Where(q => story.Quests.Any(m => m.QuestId == (string?)q.Id))
                .Select(q => ((string)q.Id!, NativeQuestAuthoring.QuestName(q))),
            "quests" => season.Quests.Select(q => ((string)q.Id!, NativeQuestAuthoring.QuestName(q))),
            "objectives" => season.Quests.SelectMany(q =>
                q.AllConditions().Select(c => ((string)c.Id!, $"{q.QuestName}: {q.Text(c.Id) ?? c.ConditionType}"))
            ),
            "items" => season.Items.Select(i => (i.Id, i.Name)),
            _ => [],
        };
    }

    public static string? ReferenceKind(object owner, string field)
    {
        return field switch
        {
            "ChapterId" => "chapters",
            "DialogId" => "dialogs",
            "MainVariable" => "variables",
            "EntryPointId" => "entries",
            "MediaId" => owner is StoryRaidBinding { Kind: "Cinematic" } ? "cinematic" : "eventmedia",
            "QuestId" => owner is StoryAction ? "ownedquests" : "quests",
            "ConditionId" or "ConditionIds" => "objectives",
            "TraderId" => "traders",
            "ItemId" => "items",
            "StatusNotes" => "notes",
            "Image" when owner is StoryPlayback => "Image",
            "Music" or "Sound" => "Audio",
            "Target" => owner switch
            {
                StoryAction a => a.Type switch
                {
                    StoryActionType.UnlockMission => "missionLinks",
                    StoryActionType.SetVariable => "variables",
                    StoryActionType.TraderStanding => "traders",
                    StoryActionType.DiaryNote => "notes",
                    StoryActionType.SwitchDialog or StoryActionType.EmbedQuestDialog => "dialogs",
                    StoryActionType.StartCinematic => "cinematic",
                    _ => null,
                },
                StoryCondition c => c.Type switch
                {
                    "VariableValue" => "variables",
                    "QuestStatus" => "quests",
                    "QuestConditionStatus" or "CompleteCondition" or "HasItemForHandover" => "objectives",
                    "TraderReputation" or "TraderLoyalty" or "HasNewQuests" or "CurrentTrader" => "traders",
                    "HasItem" => "items",
                    "LocationTrigger" => "bindings",
                    "Skill" => "skills",
                    _ => null,
                },
                StoryNoteLink l => l.Kind switch
                {
                    "Item" => "items",
                    "Offer" => "offers",
                    "Craft" => "crafts",
                    _ => null,
                },
                _ => null,
            },
            _ => null,
        };
    }

    public static string[]? Options(object owner, string field)
    {
        return (owner, field) switch
        {
            (StoryCondition, "Type") => StoryRules.ConditionTypes.Where(t => t != "ServiceAvailable").Order().ToArray(),
            (StoryCondition, "Operator") => StoryRules.Operators.ToArray(),
            (StoryCondition, "Status") or (StoryQuest, "StatusNotes") => StoryRules.QuestStatuses.ToArray(),
            (StoryDialogLine, "Side") => ["Npc", "Player"],
            (StoryEntryPoint, "Kind") => ["InLobby", "InRaid", "ViaRadio", "ViaNotebook", "ViaIntercom"],
            (StoryRaidBinding, "Kind") => ["Trigger", "Interact", "Shoot", "Collectible", "Cinematic"],
            (StoryMedia, "Kind") => ["Image", "Audio", "Video", "Cinematic", "TraderScene"],
            (StoryNoteLink, "Kind") => ["Item", "Offer", "Craft"],
            _ => null,
        };
    }

    public static bool Visible(object owner, string field)
    {
        if (owner is StoryRaidBinding zoneBinding && field == "ZoneId")
        {
            return zoneBinding.Kind is "Trigger" or "Cinematic";
        }

        if (field == "Id")
        {
            return false;
        }

        if (owner is StoryCondition c)
        {
            if (field is "Type" or "InRaidOnly")
            {
                return true;
            }

            if (c.Type is "All" or "Any" or "Not")
            {
                return field == "Conditions";
            }

            return field switch
            {
                "Conditions" or "QuestId" => false,
                "Target" => c.Type is not ("Level" or "HasFreeSpecialSlot"),
                "Status" => c.Type == "QuestStatus",
                "Value" or "Operator" => c.Type is not ("QuestStatus" or "CurrentTrader" or "Location"),
                _ => true,
            };
        }

        if (owner is StoryAction a)
        {
            return field switch
            {
                "Type" => true,
                "QuestId" => a.Type
                    is StoryActionType.SelectQuest
                        or StoryActionType.AcceptQuest
                        or StoryActionType.HandoverItem
                        or StoryActionType.FinishQuest
                        or StoryActionType.FailQuest
                        or StoryActionType.PlayerReward,
                "ConditionId" => a.Type == StoryActionType.HandoverItem,
                "Value" or "Scope" => a.Type == StoryActionType.SetVariable,
                "StandingChange" => a.Type == StoryActionType.TraderStanding,
                "Target" => a.Type
                    is StoryActionType.UnlockMission
                        or StoryActionType.SetVariable
                        or StoryActionType.TraderStanding
                        or StoryActionType.DiaryNote
                        or StoryActionType.SwitchDialog
                        or StoryActionType.EmbedQuestDialog
                        or StoryActionType.SelectSubService
                        or StoryActionType.CompleteItem
                        or StoryActionType.StartCinematic,
                _ => false,
            };
        }

        if (owner is StoryRaidBinding b && field is "ItemId" or "ObjectPath")
        {
            return (field == "ItemId") == (b.Kind == "Collectible");
        }

        if (owner is StoryMedia resource && field == "TraderId")
        {
            return resource.Kind == "TraderScene";
        }

        if (owner is StoryNoteLink l && field == "TraderId")
        {
            return l.Kind == "Offer";
        }

        return true;
    }

    public static object Create(Type type)
    {
        var value = Activator.CreateInstance(type)!;
        type.GetProperty("Id")?.SetValue(value, NewId());
        if (value is StoryChapter chapter)
        {
            chapter.Name = "New chapter";
        }

        if (value is StoryNote note)
        {
            note.Text = "New journal entry";
        }

        if (value is StoryDialogLine line)
        {
            line.Text = "New reply";
        }

        if (value is StoryRandomGate gate)
        {
            gate.VariableId = "choice";
            gate.Maximum = 2;
        }

        return value;
    }

    public static IReadOnlyList<string> Uses(SeasonDefinition season, object removed)
    {
        var ids = ModelGraph.Texts(removed).Where(t => t.IsIdentity && SeasonValidator.IsId(t.Value)).Select(t => t.Value).ToHashSet();
        return ModelGraph
            .Texts(season)
            .Where(t => ids.Contains(t.Value) && !t.IsIdentity && !t.Ancestors.Any(a => ids.Contains(ModelGraph.Id(a) ?? "")))
            .Select(t => t.Path)
            .Distinct()
            .ToArray();
    }

    public static object Duplicate(object source)
    {
        var copy = Newtonsoft.Json.JsonConvert.DeserializeObject(Newtonsoft.Json.JsonConvert.SerializeObject(source), source.GetType())!;
        var ids = ModelGraph
            .Texts(copy)
            .Where(t => t.IsIdentity && SeasonValidator.IsId(t.Value))
            .Select(t => t.Value)
            .Distinct()
            .ToDictionary(id => id, _ => NewId());
        ModelGraph.Rewrite(
            copy,
            text =>
            {
                foreach (var pair in ids)
                {
                    if (text == pair.Key || text.StartsWith(pair.Key + " ", StringComparison.Ordinal))
                    {
                        return pair.Value + text[pair.Key.Length..];
                    }
                }

                return text;
            }
        );
        return copy;
    }

    public static StoryDialog AddConversation(SeasonDefinition season, string traderId, bool branching, string questId = "")
    {
        var story = season.Story ??= new();
        var variable = new StoryVariable { Id = NewId(), Scope = StoryVariableScope.Dialogue };
        story.Variables.Add(variable);
        var dialog = new StoryDialog
        {
            Id = NewId(),
            TraderId = traderId,
            MainVariable = variable.Id,
        };
        StoryCondition Phase(int n)
        {
            return new()
            {
                Type = "VariableValue",
                Target = variable.Id,
                Operator = "==",
                Value = n,
            };
        }

        StoryAction Set(int n)
        {
            return new()
            {
                Id = NewId(),
                Type = StoryActionType.SetVariable,
                Target = variable.Id,
                Scope = variable.Scope,
                Value = n,
            };
        }

        dialog.Lines.Add(
            new()
            {
                Id = NewId(),
                Side = "Npc",
                Text = "I have something to discuss.",
                Trigger = Phase(0),
                Actions = [Set(1)],
            }
        );
        var reply = new StoryDialogLine
        {
            Id = NewId(),
            Text = questId.Length > 0 ? "I'll take the job." : "Tell me more.",
            Trigger = Phase(1),
            Actions = [Set(2)],
        };
        if (questId.Length > 0)
        {
            reply.Actions.Insert(
                0,
                new()
                {
                    Id = NewId(),
                    Type = StoryActionType.AcceptQuest,
                    QuestId = questId,
                }
            );
        }

        dialog.Lines.Add(reply);
        dialog.Lines.Add(
            new()
            {
                Id = NewId(),
                Side = "Npc",
                Text = "Here is what you need to know.",
                Trigger = Phase(2),
                Actions = [Set(3)],
            }
        );
        dialog.Lines.Add(
            new()
            {
                Id = NewId(),
                Text = "Goodbye.",
                Trigger = Phase(3),
                Actions = [new() { Id = NewId(), Type = StoryActionType.QuitAction }],
            }
        );
        if (branching)
        {
            dialog.Lines.Add(
                new()
                {
                    Id = NewId(),
                    Text = "Maybe another time.",
                    Trigger = Phase(1),
                    Actions = [new() { Id = NewId(), Type = StoryActionType.QuitAction }],
                }
            );
        }

        story.Dialogs.Add(dialog);
        story.EntryPoints.Add(
            new()
            {
                Id = NewId(),
                DialogId = dialog.Id,
                TraderId = traderId,
            }
        );
        return dialog;
    }

    public static string Help(object owner, string field)
    {
        if (EditorFieldGuide.StoryHelp(owner, field) is { } contextual)
        {
            return contextual;
        }
        return field switch
        {
            "Name" => "Player-facing chapter title. English is used when a translation is missing.",
            "Text" when owner is StoryNote =>
                "Journal text shown after this note is published by a quest status or story action. Writing a note alone does not publish it.",
            "Text" => "The NPC line or player reply shown in the conversation. Add translations in Localization; English is the fallback.",
            "Order" => "Chapter display order, lowest first. Move earlier and Move later also update this number.",
            "ChapterId" =>
                "The chapter that groups this quest or note in the journal. Moving a quest does not automatically move its existing notes.",
            "TraderId" when owner is StoryMedia =>
                "Trader whose visit uses this room. One custom room per trader per campaign; the prefab needs an inactive root and exactly one StoryCamera.",
            "TraderId" => "The installed trader associated with this record. Select by name; the reference ID is retained for the game.",
            "DialogId" => "The conversation opened by this entry point. Its named start point determines the initial phase.",
            "StartPoint" =>
                "A name declared in the conversation's Start points. Leave blank only if the conversation defines the blank/default start point.",
            "InitialValue" =>
                "Starting integer for this variable. For a conversation's main variable, entering a named start point sets the phase to that start point's value.",
            "Side" =>
                "NPC lines run automatically when eligible. Player lines appear as selectable replies. Overlapping NPC conditions can cause an ambiguous branch.",
            "Operator" => "Compare the current fact to Value. For example, >= 5 means at least five; == 5 requires exactly five.",
            "Status" => "Accepted quest statuses. The condition passes when the quest has one of these statuses.",
            "Target" when owner is StoryCondition { Type: "Location" } =>
                "Exact runtime location key, for example woods. Match the location used by your raid context.",
            "Target" when owner is StoryCondition =>
                "The fact to test. Choices depend on the condition type; use Value or Status to specify what must be true.",
            "Target" when owner is StoryAction =>
                "The record affected by this action. Only reference types compatible with the selected action are offered.",
            "Target" when owner is StoryNoteLink =>
                "The item, trader offer or hideout craft opened by this journal link. An offer selection also sets its trader.",
            "Type" when owner is StoryCondition =>
                "Choose the test that makes this content eligible. All, Any and Not group other conditions; other types inspect player or story facts.",
            "Type" when owner is StoryAction =>
                "Choose what happens when this step runs. Actions execute in their listed order; unsupported imported actions must be fixed before publication.",
            "Value" when owner is StoryCondition { Type: "TraderReputation" } =>
                "Trader standing threshold, such as 0.2. This is reputation, not loyalty level.",
            "Value" when owner is StoryCondition { Type: "TraderLoyalty" } => "Trader loyalty level threshold, usually 1 through 4.",
            "Value" when owner is StoryCondition =>
                "Threshold used with the selected comparison: an item count, level, objective progress or variable value depending on the condition type.",
            "Kind" when owner is StoryRaidBinding =>
                "Trigger, Interact and Shoot bind to an exact scene target; Collectible uses an item template. Cinematic requests authored media.",
            "Kind" when owner is StoryEntryPoint =>
                "Where this conversation can be entered. Lobby entries use a trader; raid, radio, notebook and intercom entries need their corresponding game integration.",
            "Kind" when owner is StoryNoteLink =>
                "Choose whether this journal link points to an item, an installed trader offer or a hideout craft.",
            "Kind" when owner is StoryMedia =>
                "The Unity asset's purpose. Browser previews show text and PNG artwork; Unity playback must be checked in game.",
            "Location" => "Exact runtime map key, for example woods. This limits which raid can activate the binding.",
            "Scene" =>
                "Exact Unity scene name required for entry; blank allows any scene. Raid entries use the bound object�s scene; lobby entries use the trader screen�s scene.",
            "EntryPointId" => "Optional conversation entry opened by this raid event when its conditions pass.",
            "MediaId" =>
                "Play installed image, audio, video or cinematic media when this event is accepted, before its conversation. Survival settings control progression separately.",
            "ItemId" => "The collectible's item template. This identifies an existing item; it does not spawn loot.",
            "Links" => "Optional item, offer or craft shortcuts attached to the journal note.",
            "ConditionId" => "The handover objective to complete. Choose a compatible objective belonging to the selected owned quest.",
            "Playback" =>
                "Optional presentation cues for this line. Times are in seconds; confirm final audio and animation behavior in game.",
            "Animations" or "SecondaryAnimations" or "LipSyncs" =>
                "Ordered animation cues using the trader room's exact animation keys and time intervals in seconds.",
            "Subtitles" => "Subtitle localization keys with start and end times in seconds. Add the corresponding text in Localization.",
            "Start" or "End" when owner is StoryRandomGate =>
                "Inclusive accepted range within the random draw. With Maximum 2, use 0 to 0 for one branch and 1 to 1 for the other.",
            "Group" => "Gates sharing this group and draw name reuse one random draw. Use the same Maximum across the group.",
            "Visibility" => "Controls whether the player can see this content. Empty All means always visible.",
            "Trigger" or "Condition" =>
                "All requires every condition; Any requires one; Not reverses exactly one condition. Empty All always passes.",
            "Main" => "Required quests determine chapter completion. Optional quests do not block it.",
            "AutoStart" => "Accept this owned quest when its start requirements and visibility pass.",
            "AutoComplete" => "Finish this owned quest and apply native rewards when its required objectives pass.",
            "Hidden" => "Hide this quest from the story objective list; its progression can still run.",
            "StatusNotes" => "Add a journal note when this quest reaches the selected status.",
            "MainVariable" => "The declared phase variable used by this conversation. Dialogue scope resets on entry.",
            "StartPoints" => "Named entry phases. Each value initializes the main variable when that start point is used.",
            "Actions" => "Run in order. An automatic NPC line should advance its phase so it cannot repeat indefinitely.",
            "Scope" => "Profile persists for this character; Session resets on reconnect; Dialogue belongs to one conversation.",
            "Confirmation" => "Optional confirmation shown before the player commits this reply.",
            "ObjectPath" =>
                "Exact scene:/Root/Child path of the actual collider or interaction object. This does not place or spawn an object.",
            "PersistOnDeath" => "Enabled commits immediately, including on death. Disabled waits for a surviving raid result.",
            "Once" => "Prevent this binding from being completed more than once for the character.",
            "Bundle" =>
                "Path beneath StoryMedia, for example packs/my-campaign/visit.bundle. Install Unity bundles separately from the campaign ZIP.",
            "Sha256" => "64 hexadecimal characters from the finalized installed bundle's SHA-256 checksum.",
            "Asset" =>
                "Exact asset name inside the finalized bundle. Assign a TraderScene to a trader to replace its built-in visit room; unassigned rooms remain unused.",
            "Key" => "Exact room animation dictionary key, or localization key for a subtitle. Keys differ between traders.",
            "Start" or "End" when owner is StorySequence =>
                "Time in seconds from the start of the line. End must be at or after Start, at most 3600 seconds.",
            "Speed" => "Playback multiplier: 1 is normal speed. Must be greater than 0 and at most 10.",
            "Volume" => "Audio volume from 0 (silent) to 1 (full volume).",
            "Random" => "One draw per variable/group. Shared groups need the same maximum; the inclusive range selects eligible lines.",
            "VariableId" when owner is StoryRandomGate =>
                "A name for this random draw, shared by related gates. This is not a declared story variable ID.",
            "Maximum" => "Exclusive upper bound of the random draw. With 2, possible values are 0 and 1.",
            "ConditionIds" => "Objectives associated with this journal note.",
            "QuestId" when owner is StoryAction =>
                "Only quests owned by this story can be changed. Blank uses the conversation's selected quest.",
            "Value" when owner is StoryAction => "Assign this integer to the variable; this does not increment it.",
            "Image" or "Icon" when owner is StoryChapter =>
                "Optional campaign-owned PNG artwork. Use the existing artwork upload pipeline.",
            "InRaidOnly" => "This condition also requires the player to be in a raid.",
            "QuestId" => "The quest used by this record or condition. Native objectives and rewards are edited in Quests.",
            "Image" or "Music" or "Sound" => "Optional story media reference for this line. Choose a matching image or audio asset.",
            _ => "",
        };
    }
}
