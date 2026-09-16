using WTT.Campaigns.Shared.Seasons;
using WTT.Campaigns.Shared.Story;

namespace WTT.Campaigns.Server.Web.Authoring;

// Presentation metadata only: never normalize or discard imported authoring data.
public static class EditorFieldGuide
{
    public static string NativeTitle(string? kind)
    {
        var title = StoryAuthoring.Friendly((kind ?? "").Replace('_', ' ').Replace("multiplicator", "multiplier"));
        return title.Length == 0 ? "Settings" : char.ToUpperInvariant(title[0]) + title[1..];
    }

    public static string NativeKind(object value)
    {
        return value switch
        {
            NativeCondition c => c.ConditionType,
            NativeConditionDistance => "Kills",
            NativeReward r => r.Type,
            WTT.Campaigns.Shared.Effects.PerkEffect e => e.EffectId ?? "",
            _ => "Imported settings",
        };
    }

    public static string NativeLabel(object value, string field)
    {
        return field switch
        {
            "value" when value is NativeConditionDistance => "Distance (metres)",
            "value" => NativeKind(value) switch
            {
                "Level" => "Player level",
                "TraderLoyalty" => "Loyalty level",
                "Experience" => "Experience points",
                "TraderStanding" => "Reputation change",
                "Skill" => "Skill points",
                "CounterCreator" => "Required event count",
                "FindItem" or "HandoverItem" or "HasItem" or "LeaveItemAtLocation" or "Item" => "Item quantity",
                _ => "Value",
            },
            "target" => NativeKind(value) switch
            {
                "Quest" => "Prerequisite quest",
                "TraderLoyalty" or "TraderStanding" or "TraderUnlock" => "Trader",
                "FindItem" or "HandoverItem" or "HasItem" or "LeaveItemAtLocation" or "UseItem" => "Accepted items",
                "Kills" or "Shots" => "Target type",
                "Location" => "Allowed maps",
                "ExitStatus" => "Accepted raid outcomes",
                "ExitName" => "Extraction names",
                "Skill" => "Skill",
                _ => "Target",
            },
            "onlyFoundInRaid" => "Require found in raid",
            "minDurability" => "Minimum durability (%)",
            "maxDurability" => "Maximum durability (%)",
            "availableAfter" => "Delay (seconds)",
            "plantTime" => "Planting time (seconds)",
            "oneSessionOnly" => "Complete in one raid",
            "_tpl" => "Item template",
            "StackObjectsCount" => "Stack quantity",
            "compareMethod" => "Comparison",
            _ => StoryAuthoring.Friendly(field.Replace("multiplicator", "multiplier")),
        };
    }

    public static string NativeGroup(string field)
    {
        return field switch
        {
            "target" or "value" or "traderId" or "_tpl" or "compareMethod" => "Target and amount",
            "onlyFoundInRaid" or "minDurability" or "maxDurability" or "dogtagLevel" or "isEncoded" => "Accepted item quality",
            "weapon"
            or "weaponModsInclusive"
            or "weaponModsExclusive"
            or "bodyPart"
            or "distance"
            or "enemyEquipmentInclusive"
            or "enemyEquipmentExclusive" => "Combat restrictions",
            "zoneId" or "location" or "locations" or "plantTime" or "availableAfter" or "oneSessionOnly" => "Location and timing",
            "items" or "upd" or "count" or "StackObjectsCount" => "Item contents",
            "include" or "exclude" or "skillIds" or "traderIds" or "bodyPartTypes" => "Affected targets",
            "multiplicator" or "multiplicatorPrimary" or "multiplicatorSecondary" or "intValue" => "Effect strength",
            _ => "Additional settings",
        };
    }

    public static string NativeHelp(object value, string field)
    {
        var kind = NativeKind(value);
        var amount = ModelGraph.Properties(value).FirstOrDefault(p => ModelGraph.Name(p) == field)?.GetValue(value)?.ToString() ?? "";
        if (field is "multiplicator" or "multiplicatorPrimary" or "multiplicatorSecondary")
        {
            var behavior = kind switch
            {
                "energy_drain_multiplicator" => "Energy depletion rate. Lower values make energy last longer.",
                "hydration_drain_multiplicator" => "Hydration depletion rate. Lower values make hydration last longer.",
                "stamina_consumption_body_parts_multiplicator" =>
                    "Stamina consumption in the selected arms or legs pool. Lower values reduce stamina use.",
                "stamina_restore_body_parts_multiplicator" =>
                    "Stamina recovery in the selected arms or legs pool. Higher values restore stamina faster.",
                "craft_time_multiplicator" => "Hideout crafting duration. Lower values finish crafts faster.",
                "skill_experience_multiplicator" => "Experience gain for the selected skills. Higher values increase skill progression.",
                "pmc_experience_multiplicator" => "PMC experience gain. Higher values increase character experience earned.",
                "sprint_speed_multiplicator" => "Sprinting speed. Higher values make the player sprint faster.",
                "fall_damage_multiplicator" => "Damage from falling. Lower values reduce fall damage.",
                "bleeding_chance_multiplicator" => "Chance of bleeding. Lower values reduce the chance.",
                "fracture_chance_multiplicator" => "Chance of a fracture. Lower values reduce the chance.",
                "item_resource_drain_multiplicator" =>
                    "Resource consumption for the selected item filters. Lower values reduce resource use.",
                _ => $"Multiplier for {NativeTitle(kind)}. Whether a lower value helps depends on this effect.",
            };
            return behavior + $" Current multiplier: {amount}. 1 keeps the normal value, 0.8 is 20% lower and 1.2 is 20% higher.";
        }
        if (field == "intValue" && kind is "skill_level_preset" or "skill_max_level_cap" or "stamina_scale_body_parts")
        {
            return kind switch
            {
                "skill_level_preset" =>
                    $"Starting level assigned to the selected skills: {amount}. This sets a level rather than increasing experience gain.",
                "skill_max_level_cap" => $"Maximum level the selected skills may reach: {amount}.",
                _ =>
                    $"Stamina capacity offset for the selected arms or legs pool: {amount}. This is a whole-number offset, not a multiplier.",
            };
        }
        if (field == "value" && (value as WTT.Campaigns.Shared.Effects.ItemFilterRule)?.Field is "_tpl" or "ParentId")
        {
            return (value as WTT.Campaigns.Shared.Effects.ItemFilterRule)?.Field == "_tpl"
                ? "Select the individual item matched by this filter."
                : "Select the item category matched by this filter. All items in that category are affected.";
        }

        if (field == "value" && value is NativeConditionDistance)
        {
            return $"Distance between shooter and target in metres. The current test is {(value as NativeConditionDistance)?.CompareMethod ?? ">="} {amount} metres.";
        }

        if (field == "value")
        {
            return kind switch
            {
                "Level" => $"Compare the player's level against {amount}. Use at least (>=) for a minimum level requirement.",
                "TraderLoyalty" =>
                    $"Required loyalty level with the selected trader. Current threshold: {amount}; normal levels are 1 through 4.",
                "FindItem" =>
                    $"Number of accepted items the player must find. Current requirement: {amount}. Item quality restrictions below also apply.",
                "HandoverItem" =>
                    $"Number of accepted items to hand over to the quest trader. Current requirement: {amount}; handed-in items are consumed.",
                "HasItem" => $"Number of accepted items the player must possess. Current requirement: {amount}.",
                "LeaveItemAtLocation" => $"Number of accepted items to place at the configured zone. Current requirement: {amount}.",
                "Salvage" =>
                    "One completed CommonLib salvage interaction satisfies this objective. Leave this value at 1; configure reward quantities on the zone.",
                "CounterCreator" => $"Number of qualifying events required: {amount}. The nested filters define which events count. "
                    + (
                        (value as NativeCondition)?.OneSessionOnly == true
                            ? "Progress must be made in one raid."
                            : "The counter can accumulate across raids."
                    ),
                "Experience" => $"Grants {amount} character experience points when this reward's stage is reached. Use a whole number.",
                "TraderStanding" =>
                    $"Changes the selected trader's reputation by {amount}. Positive values add standing; negative values remove it (for example, 0.02).",
                "Skill" => $"Skill points granted to the selected skill: {amount}. This is a reward amount, not a required player level.",
                "Item" => "Native item reward amount. Individual inventory stack quantities are configured under Item contents.",
                _ =>
                    $"Numeric value for {StoryAuthoring.Friendly(kind)}. Consult the imported definition for the units used by this native type.",
            };
        }

        if (field == "target")
        {
            return kind switch
            {
                "Quest" => "Select the prerequisite quest. Required quest status and the delay below decide when this requirement passes.",
                "HandoverItem" => "Item templates the trader accepts for hand-in. Quantity and item quality are configured separately.",
                "FindItem" =>
                    "Item templates that count as finds for this objective. Require found in raid adds the raid-origin restriction.",
                "HasItem" or "LeaveItemAtLocation" or "UseItem" =>
                    $"Accepted item templates for {StoryAuthoring.Friendly(kind)}. Select each item by name.",
                "TraderStanding" => "Trader whose reputation changes when this reward is granted.",
                "TraderUnlock" => "Trader made available by this reward.",
                "TraderLoyalty" => "Trader whose loyalty level is tested against the required level.",
                "Kills" or "Shots" =>
                    "Native target category (for example Any). Combat restrictions further constrain which targets and hits qualify.",
                "Location" => "Runtime map keys where the counter can progress, for example woods. These are map keys, not item IDs.",
                "ExitStatus" => "Accepted native raid outcomes for this counter, for example Survived.",
                "ExitName" => "Exact extraction names that qualify for this counter.",
                "Skill" => "Skill that receives the points from this reward.",
                _ =>
                    $"Target reference for {StoryAuthoring.Friendly(kind)}. Preserve the native reference format when editing imported content.",
            };
        }

        return field switch
        {
            "onlyFoundInRaid" => ModelGraph.Properties(value).FirstOrDefault(p => ModelGraph.Name(p) == field)?.GetValue(value) as bool?
            == true
                ? $"Only found-in-raid items qualify for this {StoryAuthoring.Friendly(kind)} objective."
                : "Items can qualify without a found-in-raid mark. Enable to restrict accepted items to found in raid.",
            "minDurability" or "maxDurability" =>
                $"Accept items with durability from {(value as NativeCondition)?.MinDurability ?? 0}% to {(value as NativeCondition)?.MaxDurability ?? 100}%. Minimum must not exceed maximum.",
            "availableAfter" => $"Wait {amount} seconds after the prerequisite reaches the selected status. Zero adds no delay.",
            "oneSessionOnly" => ModelGraph.Properties(value).FirstOrDefault(p => ModelGraph.Name(p) == field)?.GetValue(value) as bool?
            == true
                ? "All required counter progress must be earned in one raid."
                : "Counter progress can accumulate across raids. Enable for a single-raid challenge.",
            "plantTime" => kind == "Salvage"
                ? "Salvage description time. The zone's salvage time controls the actual interaction duration."
                : $"The player must spend {amount} seconds placing an item at this objective's zone.",
            "zoneId" =>
                $"Exact in-game zone identifier for {StoryAuthoring.Friendly(kind)}. This is a zone inside a map, not the map name.",
            "weapon" => "Only events using these weapon templates count. An empty list applies no weapon restriction.",
            "weaponModsInclusive" =>
                "Required weapon attachment templates for a qualifying event. Preserve nested alternative groups from imported quests.",
            "weaponModsExclusive" => "Weapon attachment templates that disqualify an event.",
            "bodyPart" => "Body regions that qualifying hits must affect. An empty list applies no body-part restriction.",
            "compareMethod" =>
                $"Comparison used by {StoryAuthoring.Friendly(kind)}: {amount}. >= accepts values at or above the threshold; == requires an exact match.",
            "include" or "exclude" =>
                $"{(field == "include" ? "Allow" : "Exclude")} the listed item filters for {StoryAuthoring.Friendly(kind)}. Each entry chooses an item or category.",
            "field" =>
                "Choose whether this filter matches an individual item template or a whole item category. The value picker follows this selection.",
            "multiplicator" or "multiplicatorPrimary" or "multiplicatorSecondary" =>
                $"Multiplier for {StoryAuthoring.Friendly(kind)}. 1 is unchanged, 0.8 is 20% lower and 1.2 is 20% higher. A lower value is beneficial only for effects where less is better.",
            "intValue" =>
                $"Whole-number setting for {StoryAuthoring.Friendly(kind)}. The selected effect determines whether this is a level, capacity or offset; validation checks its supported range.",
            "_tpl" => "Inventory item template. Select by name; attachments retain their own item templates.",
            "StackObjectsCount" or "count" => $"Number of units in this inventory stack: {amount}. Use a positive whole number.",
            "dogtagLevel" => $"Minimum player level recorded on an accepted dogtag: {amount}.",
            "skillIds" => $"Skills affected by {StoryAuthoring.Friendly(kind)}. Select each skill that should receive this modifier.",
            "traderId" or "traderIds" => $"Trader selection for {StoryAuthoring.Friendly(kind)}. Search using the trader's name.",
            "bodyPartTypes" => "Stamina pool affected by this modifier: arms or legs.",
            _ =>
                $"{NativeLabel(value, field)} belongs to {StoryAuthoring.Friendly(kind)}. This imported setting is preserved; check its native definition before changing unfamiliar values.",
        };
    }

    public static string StoryGroup(object owner, string field)
    {
        if (owner is StoryAction && field is "Target" or "Value" or "QuestId" or "ConditionId")
        {
            return "Action details";
        }
        return field switch
        {
            "Type" or "Kind" or "Scope" or "InitialValue" => "Behavior",
            "Name" or "Text" or "Side" or "Confirmation" => "Player-facing content",
            "Image" or "Icon" or "Playback" or "Music" or "Sound" => "Artwork and playback",
            "Visibility" or "Trigger" or "Condition" or "Conditions" or "InRaidOnly" or "Random" => "When this applies",
            "Target" or "Value" or "Operator" or "Status" or "ConditionId" => "Target and comparison",
            "Actions" or "AutoStart" or "AutoComplete" or "PersistOnDeath" or "Once" => "Progression and outcomes",
            "ChapterId" or "QuestId" or "ConditionIds" or "StatusNotes" or "Links" or "Main" or "Hidden" or "Order" =>
                "Organization and links",
            "TraderId" or "DialogId" or "StartPoint" or "StartPoints" or "MainVariable" or "EntryPointId" => "Conversation routing",
            "Location" or "Scene" or "ObjectPath" or "ItemId" or "MediaId" => "Raid event target",
            "Bundle" or "Asset" or "Sha256" => "Media source",
            _ => "Settings",
        };
    }

    public static string StoryLabel(object owner, string field)
    {
        return (owner, field) switch
        {
            (StoryCondition c, "Value") => c.Type switch
            {
                "Level" => "Player level",
                "TraderReputation" => "Reputation threshold",
                "TraderLoyalty" => "Loyalty level",
                "HasItem" or "HasItemForHandover" => "Required item count",
                "HasFreeSpecialSlot" => "Free special slots",
                "CompleteCondition" or "QuestConditionStatus" or "HasNewQuests" or "LocationTrigger" or "CompletableItem" =>
                    "Expected state (0 or 1)",
                "VariableValue" => "Variable threshold",
                "Skill" => "Skill level",
                "HideoutArea" => "Area level",
                _ => "Value",
            },
            (StoryAction { Type: StoryActionType.SetVariable }, "Value") => "Assign value",
            (StoryAction { Type: StoryActionType.TraderStanding }, "Target") => "Trader",
            (StoryAction, "StandingChange") => "Reputation change",
            (StoryCondition, "Type") => "Condition type",
            (StoryAction, "Type") => "Action type",
            _ => StoryAuthoring.Friendly(field),
        };
    }

    public static string? StoryHelp(object owner, string field, SeasonDefinition? season = null)
    {
        if (owner is StoryVariable variable && field is "Scope" or "InitialValue")
        {
            return $"This {variable.Scope} variable begins at {variable.InitialValue}. "
                + (
                    variable.Scope == StoryVariableScope.Profile ? "Its value persists for this character."
                    : variable.Scope == StoryVariableScope.Session ? "Its value resets on reconnect."
                    : "Its value belongs to the current conversation."
                );
        }
        if (owner is StoryCondition c)
        {
            var subject = c.Type switch
            {
                "Level" => "player level",
                "TraderReputation" => "selected trader's reputation",
                "TraderLoyalty" => "selected trader's loyalty level",
                "HasItem" => "number of the selected item in inventory",
                "HasItemForHandover" => "number of items eligible for the selected handover objective",
                "HasFreeSpecialSlot" => "number of empty special slots",
                "VariableValue" => "selected variable's current value",
                "Skill" => "selected skill's level",
                "HideoutArea" => "selected hideout area's level",
                "CompleteCondition" or "QuestConditionStatus" => "objective completion (1 = complete, 0 = incomplete)",
                "HasNewQuests" => "new quests from this trader (1 = available, 0 = none)",
                "LocationTrigger" => "raid trigger state (1 = reached, 0 = not reached)",
                "CompletableItem" => "story item completion (1 = completed, 0 = incomplete)",
                _ => StoryAuthoring.Friendly(c.Type),
            };
            if (field is "Value" or "Operator")
            {
                return $"Tests {subject} using {c.Operator} {c.Value}. "
                    + (
                        c.Type == "TraderReputation" ? "Reputation uses decimal values, for example 0.2."
                        : c.Type == "VariableValue" ? VariableHelp(season, c.Target)
                        : "Choose a threshold and comparison that match the intended requirement."
                    );
            }

            if (field == "Type")
            {
                return c.Type switch
                {
                    "All" => "Every child condition must pass. An empty All group always passes.",
                    "Any" => "At least one child condition must pass. An empty Any group never passes.",
                    "Not" => "Reverses exactly one child condition. Add one child test to specify what must not be true.",
                    "QuestStatus" => "Checks whether the selected quest has any of the accepted statuses below.",
                    "CurrentTrader" => "Passes only while interacting with the selected trader.",
                    "Location" => "Requires an active raid on the exact map key entered below.",
                    _ => $"Checks {subject}. "
                        + (
                            c.InRaidOnly
                                ? "This test also requires an active raid."
                                : "The In raid only option can restrict this test to raids."
                        ),
                };
            }

            if (field == "Target")
            {
                return c.Type switch
                {
                    "VariableValue" => "Choose the variable whose current value is compared. " + VariableHelp(season, c.Target),
                    "QuestStatus" => "Quest whose status is checked. Any one of the accepted statuses below can satisfy the condition.",
                    "CompleteCondition" or "QuestConditionStatus" =>
                        "Objective whose completion is tested as 1 (complete) or 0 (incomplete). This checks completion, not partial progress.",
                    "HasItemForHandover" => "Handover objective whose eligible item count is compared with the required amount.",
                    "HasItem" => "Item template to count in the player's inventory.",
                    "Location" => "Exact runtime map key, for example woods. This condition requires the player to be in that raid.",
                    "LocationTrigger" => "Raid binding whose reached state is tested.",
                    "CompletableItem" => "Story item key recorded by a Complete item action. Its completion is tested as 1 or 0.",
                    "Skill" => "Skill whose level is compared with the required threshold.",
                    "HideoutArea" => "Native hideout area key whose level is compared with the required threshold.",
                    "CurrentTrader" => "Trader the player must currently be interacting with.",
                    "HasNewQuests" => "Trader to check for newly available quests.",
                    _ => $"Select the trader used to test the {subject}.",
                };
            }
        }
        if (owner is StoryAction a)
        {
            if (a.Type == StoryActionType.TraderStanding)
            {
                return "Adds reputation to the selected trader. Use a positive decimal to increase standing (0.02) or a negative decimal to decrease it (-0.02).";
            }
            if (a.Type == StoryActionType.SetVariable && field is "Target" or "Value" or "Scope")
            {
                return $"Assigns {a.Value} to the selected variable; it does not increment it. " + VariableHelp(season, a.Target);
            }

            if (field is "Type" or "Target" or "QuestId")
            {
                return a.Type switch
                {
                    StoryActionType.DiaryNote => "Publishes the selected journal note when this action runs.",
                    StoryActionType.SwitchDialog =>
                        "Switches to the selected conversation. Configure its entry phases in Conversation routing.",
                    StoryActionType.EmbedQuestDialog => "Embeds the selected quest conversation in the current story flow.",
                    StoryActionType.AcceptQuest => "Accepts the selected owned quest when its start requirements allow it.",
                    StoryActionType.FinishQuest => "Finishes the selected owned quest when its required objectives allow completion.",
                    StoryActionType.FailQuest =>
                        "Fails the selected active owned quest through the native quest system, including its failure rewards. Completed or unstarted quests cannot be failed.",
                    StoryActionType.HandoverItem =>
                        "Hands eligible items to the selected owned quest's handover objective. Choose that objective below.",
                    StoryActionType.PlayerReward => "Requests the native rewards for the selected owned quest through the quest adapter.",
                    StoryActionType.StartCinematic => "Plays the selected cinematic or video media reference.",
                    _ => null,
                };
            }
        }
        return null;
    }

    private static string VariableHelp(SeasonDefinition? season, string id)
    {
        var variable = season?.Story?.Variables.FirstOrDefault(v => v.Id == id);
        return variable == null
            ? "Select a declared variable to see its scope and initial value."
            : $"Selected variable: {variable.Scope} scope, initial value {variable.InitialValue}. "
                + (
                    variable.Scope == StoryVariableScope.Profile ? "Its value persists for this character."
                    : variable.Scope == StoryVariableScope.Session ? "Its value resets on reconnect."
                    : "Its value belongs to the current conversation."
                );
    }
}
