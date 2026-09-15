namespace WTT.Campaigns.Server.Web.Authoring;

public static class CreatorGuidance
{
    public static string SectionTitle(string section)
    {
        return section switch
        {
            "Chapters" => "Chapters and quests",
            "Quests" => "Non-story quests",
            "Trader offers" => "Trader assortments",
            _ => section,
        };
    }

    public static readonly CreatorHelpTopic[] Topics =
    [
        new(
            "Trader offers",
            "Choose a trader and enter edit mode to change their assortment.",
            [
                "Choose a trader, review their installed assortment, then select Edit assortment. Edit or remove existing offers, add new ones, or clear the assortment and start empty. Clear and restore actions can be undone.",
                "New offers inherit the selected trader. Configure loyalty, payment, stock and purchase limits in the offer editor. Unlock-only offers also need an enabled reward that references them.",
                "Enable Web item previews in the game, remain at the main menu and choose that client in the editor. Wait for a verified assembly before publishing.",
                "Use cached images to keep editing offline. Refresh after repairing missing bundles. Prices and stock do not invalidate assembly verification.",
            ],
            "Campaign edits affect only that campaign. Open the standalone trader editor from the Creator header to edit and export assort.json independently. Native access, loyalty and restock rules still apply. Fence is not supported."
        ),
        new(
            "Overview",
            "Give the campaign an identity and introduce it to players.",
            [
                "Create a blank draft in the library, or duplicate a campaign to use its content as a starting point.",
                "Enter a name, author, version and description. Choose badge/banner artwork and add introductory slides.",
                "Use Save changes to keep the draft. Editing does not change published packs or characters.",
            ],
            "Practice in a separate draft. A campaign already used by characters needs a new identity for gameplay changes."
        ),
        new(
            "Starting character",
            "Choose the starting equipment and skills for each faction.",
            [
                "Choose a starter preset, or leave it blank to use the account's edition.",
                "Select USEC or BEAR before editing. Add an item, choose its quantity, then choose Stash or an equipment slot.",
                "Repeat for the other faction. Add skills and set starting levels from 0 to 51.",
            ],
            "Stash items are added. Equipment replaces the selected slot. Perk grants apply afterward."
        ),
        new(
            "Perks",
            "Create benefits, drawbacks and rules shared by campaign characters.",
            [
                "Add a personal perk or common perk, then select it in the content picker.",
                "Enter its name and description and select an implemented effect template. Adjust the effect's parameters.",
                "For personal perks, set points and conflicts. For common perks, choose whether it applies to every campaign character.",
                "Check the perk budget under Preview and publish.",
            ],
            "A benefit costing one point uses -1. A drawback adding one point uses +1. A multiplier of 1 is unchanged; 1.2 is 20% higher."
        ),
        new(
            "Documents",
            "Configure document collection and the currency used by rewards.",
            [
                "Set documents per raid, collection allowance and window duration in seconds.",
                "Select each document type and choose its item template and available/unavailable artwork.",
                "Add map overrides only where the per-raid cap should differ.",
            ],
            "The raid cap is eight. The default collection window is 82,800 seconds (23 hours). Map overrides replace the default cap."
        ),
        new(
            "Battle pass",
            "Arrange rewards into pages players unlock in order.",
            [
                "Select or add a page, then add a reward to an empty cell.",
                "Select the tile to edit its name, artwork, costs, requirements and contents below the grid.",
                "Drag tiles to move them, or edit width and height to resize. Add further pages as needed.",
                "Set Required claims from previous page to zero on page one.",
            ],
            "Page gates count enabled tiles on the immediately preceding page. Tiles cannot overlap or extend outside the 2 × 3 grid."
        ),
        new(
            "Rewards",
            "Build the campaign's separate reward grid.",
            [
                "Add a reward and select its tile.",
                "Use the selected reward editor below the grid to configure costs, requirements and one or more reward payloads.",
                "Arrange the tiles within the 5 × 2 grid. Check eligibility under Preview and publish.",
            ],
            "Disabled tiles cannot be claimed. Document costs and prerequisites both need to pass."
        ),
        new(
            "Items and crates",
            "Create campaign-owned items and configure exchanges and crate contents.",
            [
                "Create an item, give it a name, and choose an installed source model.",
                "Set its dimensions and stack limit. For a crate, choose a native loot-container source model.",
                "Add a crate definition using the owned item, choose the number of rolls, then add weighted pool entries.",
                "Select an exchange crate to enable that exchange, or leave it blank.",
            ],
            "Weights are relative: weight 2 is twice as likely as weight 1. A crate definition and the crate item are separate records."
        ),
        new(
            "Quests",
            "Create non-story quests independently of the chapter workspace.",
            [
                "Add a non-story quest. In Basics, name it, write its description and choose a trader.",
                "In Unlock requirements, configure when the quest becomes available. In Objectives, configure completion rules. New quests include a Level requirement and an unfinished HandoverItem objective; choose its target item.",
                "In Rewards, add completion rewards. Non-story quests have no story behavior tab.",
                "To convert a quest into a story quest, expand Add to a story chapter. Assign a chapter to move it into that chapter workspace.",
            ],
            "Auto start accepts an eligible quest. Auto complete finishes it and grants native rewards once required objectives pass. Raid counter filters belong inside CounterCreator."
        ),
        new(
            "Missions",
            "Link a playable route to a story quest.",
            [
                "Open Missions after creating a map layout and a story-backed quest. Add a mission, then enter its name and briefing.",
                "Choose the authored layout, story quest and an AvailableForFinish GlobalVariableValue objective. Mission completion sets that profile story variable through the native quest condition.",
                "Open Layouts to author a start, ordered checkpoints and an exit. Use Mission start for optional authored encounters; layout-owned zones are available to that mission's quest context.",
                "Save and validate before publishing. Accept the linked quest in the lobby, deploy from the native Missions screen, complete the route and extract before turning in the quest.",
            ],
            "Retries begin at the start. Ordinary raids retain their normal extracts and encounter behavior. Replays keep ordinary loot and experience rules."
        ),
        new(
            "Chapters",
            "Create and edit story quests inside their chapter.",
            [
                "Choose Add story chapter when starting a story, or Add chapter when a story already exists.",
                "Use Chapter settings to name the chapter and optionally select its image and icon.",
                "Leave Visibility as empty All for an always-visible chapter, or add conditions.",
                "Use + Create quest beneath a chapter in the tree, or select an existing quest by name. Follow Basics, Unlock requirements, Objectives, Rewards, Story events and Preview. Select the chapter name to edit chapter settings.",
            ],
            "Required quests determine chapter completion. A chapter with no required quests does not automatically become complete."
        ),
        new(
            "Journal notes",
            "Write entries that reveal information as the story progresses.",
            [
                "Add a journal note, select its Chapter, and write its Text.",
                "Optionally associate objective IDs or add Item, Offer or Craft links.",
                "Reveal the note with a Diary note action in a conversation/raid event, or add it to a quest's Status notes.",
            ],
            "Creating a note does not reveal it. Status notes use named quest states such as Started or Success."
        ),
        new(
            "Conversations",
            "Write NPC dialogue and player replies with conditions and actions.",
            [
                "Open Conversation templates, choose a trader, and create a Simple conversation, Branching exchange or Quest-linked conversation.",
                "Select a line in the dialogue outline and edit its Text under Write. NPC lines run automatically; Player lines are selectable replies.",
                "Use Conditions to control when a line is available and Effects to change state. Effects execute from top to bottom. Guided connections are available for Dialogue-scope phase templates.",
                "Use Connections to inspect the flow, Preview to review text, and Story rehearsal to try the choices.",
            ],
            "Templates create a phase variable and entry point. Keep the phase-changing actions: a repeating NPC line or two eligible NPC lines will roll back the conversation step."
        ),
        new(
            "Variables",
            "Remember decisions and control the phase of a conversation.",
            [
                "Add a variable and choose its scope and initial integer value.",
                "Use a Variable value condition to test it, for example equals 1.",
                "Use a Set variable action with the same scope to assign a new value.",
            ],
            "Profile persists for the character. Session resets on reconnect. Dialogue belongs to a conversation. Set variable assigns a value; it does not add to the previous value."
        ),
        new(
            "Entry points",
            "Make conversations available in the lobby or during raids.",
            [
                "Select the entry point created by a template, or add one manually.",
                "Choose the dialogue and matching trader. Use InLobby for a trader visit.",
                "Leave Start point blank to use the default phase, or choose a name declared in the dialogue's Start points.",
                "Add availability conditions if the conversation should be gated.",
            ],
            "InRaid, ViaRadio, ViaNotebook and ViaIntercom require raid context. Their world interaction is connected separately through raid events."
        ),
        new(
            "Raid events",
            "Connect story progression to an existing world interaction.",
            [
                "Add a raid event, choose its location and kind, and enter a verified scene target or collectible item template.",
                "Configure its condition and actions. Optionally link a conversation entry point or media.",
                "Choose Once and Persist on death to control repetition and when changes commit.",
                "Rehearse survival/death outcomes, then verify the actual target in-game.",
            ],
            "Use an exact scene:/Root/Child path. Bindings do not spawn objects or loot. With Persist on death disabled, actions wait for a surviving raid result."
        ),
        new(
            "Story media",
            "Register playback assets installed separately on the client.",
            [
                "Add a media reference and select Image, Audio, Video, Cinematic or TraderScene.",
                "Enter the bundle path beneath StoryMedia, its exact asset name, and the finalized bundle's 64-character SHA-256 checksum.",
                "Reference the media from a line's Playback or a cinematic action. Set animation/subtitle timing in seconds.",
            ],
            "PNG chapter art uses the normal upload control. Unity bundles are installed separately and are not included in Creator ZIPs. Room animation keys differ by trader."
        ),
        new(
            "Story rehearsal",
            "Try story progression without opening a player profile.",
            [
                "Set simulated player facts and a random seed, then Start rehearsal.",
                "Choose an available entry point and select player replies. Inspect the journal and event log.",
                "For handovers, set both Items (inventory by template) and Handover items (eligible count by objective), then Apply simulated facts.",
                "For unmodeled native behavior, choose Simulate success or Simulate failure. Restart rehearsal after editing the draft.",
            ],
            "Rewards and media are recorded, not played or granted. A failed step rolls back. World detection, item filters and Unity playback still need in-game checks."
        ),
        new(
            "Localization",
            "Translate text while keeping English as the fallback.",
            [
                "Choose a language or enter a new installed language code, such as fr.",
                "Filter by text or ID, then edit the matching entries.",
                "Keep English names, descriptions and story text meaningful; blank translations use those fields.",
            ],
            "Editing English updates the authoritative text. A translation appears in-game only when its language code matches an installed locale."
        ),
        new(
            "Preview and publish",
            "Check the draft and prepare a shareable pack.",
            [
                "Save changes, then Validate. Follow each issue back to the relevant editor section and fix it.",
                "Simulate reward eligibility and perk budgets; use Story rehearsal for dialogue progression.",
                "Save and validate again after changes. Publish pack creates an immutable revision; Download campaign pack exports it.",
                "Restart SPT to load new packs, then choose the campaign when creating a campaign character.",
            ],
            "Each campaign character chooses its own campaign. Missing installed dependencies may allow export but prevent a pack from being playable. Keep practice packs separate from the campaign you play."
        ),
    ];
    public static readonly CreatorTutorialStep[] Basics =
    [
        new(
            "Create a practice draft",
            "Overview",
            [
                "In the library, choose Create blank campaign. If you prefer an existing setup, duplicate it instead.",
                "Name the draft Tutorial — my first campaign and enter a short description.",
                "Choose Save changes. Keep this practice draft separate from the campaign you play.",
            ],
            "The toolbar shows your draft name and Saved. Published packs and characters have not changed."
        ),
        new(
            "Choose the starting setup",
            "Starting character",
            [
                "Leave Starter preset blank to use the account edition, or select an installed preset.",
                "Select Usec. Add a starting item, search for an ordinary item or currency, set its quantity and leave Destination as Stash.",
                "Select Bear and configure its starting items too. An equipment destination replaces that slot.",
            ],
            "Both factions have the starting setup you intended."
        ),
        new(
            "Set collection rules",
            "Documents",
            [
                "Keep one or more document types and their default artwork for this first pass.",
                "Review Documents per raid (maximum 8), Collection allowance, and Collection window in seconds.",
                "For a 23-hour window use 82800 seconds. Leave map overrides empty unless a map needs a different cap.",
            ],
            "Each document has a valid template and artwork, with clear collection limits."
        ),
        new(
            "Create a reward",
            "Battle pass",
            [
                "Select page one and keep Required claims from previous page at 0.",
                "Add reward, then select its tile. Its settings appear directly below the grid.",
                "Give it a name, choose its required artwork, set a document cost and add an item payload with a quantity.",
                "Try moving the tile and changing its size within the grid.",
            ],
            "The selected reward has a cost, valid artwork and a payload, and fits inside the page."
        ),
        new(
            "Check and export",
            "Preview and publish",
            [
                "Save changes, then Validate. Fix each error and repeat after editing.",
                "Set simulated document balances and player level to check the reward's eligibility.",
                "When validation allows publication, choose Publish pack and Download campaign pack.",
                "To try this pack in-game, restart SPT and create a campaign character with this campaign. Start Your first story quest from Help and tutorials when ready.",
            ],
            "You have a saved draft and can export a published pack without changing which campaign any character belongs to."
        ),
    ];
    public static readonly CreatorTutorialStep[] Story =
    [
        new(
            "Prepare a practice campaign",
            "Overview",
            [
                "Create a blank campaign in the library, or open a separate practice draft. Name it Tutorial — supply run.",
                "This walkthrough creates a quest accepted through a trader conversation, a one-item handover, and a journal note on completion.",
                "Use the Open section button at each step. The tutorial only provides directions; you make and save the edits.",
            ],
            "A practice draft is open. No story content has been inserted automatically."
        ),
        new(
            "Create the chapter",
            "Chapters",
            [
                "Choose Add story chapter, or Add chapter if this draft already has a story.",
                "Set Name to First contact. Artwork is optional for this example.",
                "Leave Visibility as All with no child conditions so the chapter is visible.",
            ],
            "First contact appears in the chapter list."
        ),
        new(
            "Add a supply quest",
            "Chapters",
            [
                "Choose + Create quest beneath First contact in the chapter tree. In Basics, name it A small favor, describe the supply request, and choose Prapor.",
                "In Unlock requirements, keep Player level at 1. In Objectives, expand the Hand over items rule: choose one ordinary inventory item, set Value to 1, and leave Only found in raid off with durability 0–100.",
                "The quest already belongs to First contact. In Story events, open Journal visibility and chapter completion and keep Main enabled. Under Automatic quest progression, enable Auto complete and leave Auto start disabled.",
                "The quest will be accepted by a conversation. You can leave Rewards empty for this lesson.",
            ],
            "A small favor belongs to First contact and completes automatically after its one required handover."
        ),
        new(
            "Write the completion note",
            "Chapters",
            [
                "Select A small favor in the chapter tree and open Story events. Choose completed and handed in, then Create and write note.",
                "The note workspace opens. Write: Prapor received the supplies. We have made our first contact.",
                "The chapter and Success association are already assigned. You can edit this same note later in Journal notes.",
            ],
            "The note is linked to quest Success. It will appear after completion, not merely because it exists."
        ),
        new(
            "Build the trader conversation",
            "Chapters",
            [
                "Select A small favor → Story events, then expand Create a quest introduction. Keep Prapor selected and choose Create and write conversation to open its writing workspace.",
                "The template adds a conversation, phase variable and lobby entry. Edit its NPC text and acceptance reply, keeping the generated triggers and phase-changing actions.",
                "Select the final Goodbye reply in the dialogue outline. Under Write, change its Text to Here are the supplies.",
                "Under Effects, Add action and choose Handover item. Select A small favor for Quest id and its HandoverItem objective for Condition id. Move this action before Quit action.",
            ],
            "The conversation accepts the quest, then offers a handover reply. The handover runs before the conversation closes."
        ),
        new(
            "Check the entry and flow",
            "Conversations",
            [
                "Select the conversation and expand Conversation settings and availability, then Conversation availability and phase. Its entry should use InLobby, Prapor and this conversation.",
                "Leave Start point blank and Condition as empty All for this lesson.",
                "In Conversations, open Connections to inspect phase transitions. Use Preview to read the dialogue in order.",
                "The main variable uses Dialogue scope, so a new conversation starts at its initial phase.",
            ],
            "The dialogue has a matching trader entry and no ambiguous or repeating automatic NPC line."
        ),
        new(
            "Rehearse the supply run",
            "Story rehearsal",
            [
                "Expand Simulated player facts and variables. Keep Player level at 1 and In raid off.",
                "Under Items, add the quest's chosen item template and set its inventory count to 1. Under Handover items, add the handover objective and set its eligible count to 1.",
                "Start rehearsal, open the new entry point and select the acceptance reply. Then choose Here are the supplies.",
                "Inspect the event log and journal. If a handover is blocked, correct both simulated counts and click Apply simulated facts. Restart after changing the draft.",
            ],
            "The quest reaches Success, First contact completes, and the journal shows your note. Only simulated inventory changed."
        ),
        new(
            "Save and review",
            "Preview and publish",
            [
                "Save changes and Validate. Follow any issues back to the relevant content and fix them.",
                "Publish and download the practice pack when validation allows it. Restart SPT to load it, then choose it for a new campaign character.",
                "For a real story, add quest-status conditions to entry points or replies so completed jobs do not keep offering the same handover.",
                "Test the pack in-game before using it for a campaign. Add raid events, media and translations after this basic loop works.",
            ],
            "You have authored and rehearsed a complete chapter–quest–conversation–journal loop."
        ),
    ];
}
