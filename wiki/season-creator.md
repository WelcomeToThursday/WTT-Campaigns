# Campaign Creator

Open the **WTT-Campaigns creator link in the SPT launcher**, or visit [the Campaign Creator on your local server](https://127.0.0.1:6969/wtt-campaigns/creator) while SPT is running. If your server uses a different address or port, use the address from your launcher followed by `/wtt-campaigns/creator`. Sign in with an administrator account if prompted.

Use the Creator to make campaigns, configure starting characters and rewards, author quests and stories, and export packs for sharing. Install the complete matching client/server release before playing authored content.

## Workspace and help

The landing library keeps drafts and published packs in separate sections. Search by campaign name or draft description; draft cards show quest, perk and reward counts. Use **Create blank campaign** to start, or import a ZIP from the sidebar to open it as a draft. Published packs retain their revision, duplication and export actions. Import guidance and tutorials sit beside the library on wide screens and stack below it on smaller screens.

Grouped navigation stays at the left and a broad editing column holds the selected content. Help, connections and preview guidance live in a collapsed inspector below the workspace. Reward settings open directly below the reward grid. Each editor separates presentation, rules, content and advanced settings into labeled sections that stack on smaller screens.

Open **Help and tutorials** from the library or any draft for searchable instructions and a glossary. **Campaign basics** covers setup through export; **Your first story quest** walks through a chapter, native quest, completion note, conversation and rehearsal. Each editor section also includes **How to use this section**. Tutorials provide self-paced directions and navigation; they do not create or save content automatically. See the [tutorial guide](creator-tutorials.md) for a printable walkthrough and troubleshooting.

Hover or focus the **?** markers for explanations of point costs, collection windows, page gates, weighted crate contents and other settings. Tap a marker on touch devices; Escape dismisses focused help. Reward tiles show selection and disabled states. Select a tile to edit its contents and dimensions below the grid, then use **Save changes** in the top bar.

## Manage drafts

The library starts with the most recently edited drafts. Use **Sort drafts** for oldest first or name order; existing drafts use their file date until their next edit. Dates are shown in the server's local time.

Open **Manage** on a draft to **Rename**, **Duplicate**, **Archive**, or **Move to Trash**. Rename changes the draft name only. Duplication opens an independent campaign with new content identities. Archive sets a draft aside; Trash removes it from your working list after a confirmation. Both views offer **Restore to Drafts**, preserve all content, and survive a server restart. Trash is never automatically emptied; there is no permanent-delete action.

Archived and trashed drafts must be restored before editing or publishing. Published packs and characters are unaffected by organization or removal of a draft. If another tab changed or moved a draft, save and management actions reject the stale revision; use **Refresh library** to see the current state.

## Create a campaign

1. Open the creator through SPT’s web interface with an administrator account. Select **Create blank campaign**, or use **Duplicate** on the draft or published pack you want to copy. Blank campaigns use the bundled document model, neutral branding, valid artwork, one empty battle-pass page, and no selected perks.
2. Fill in Overview and Starting character. Choose an installed starter edition or leave it blank to use each account’s normal starter edition. Stash additions include money through the item picker; equipment replaces its selected slot. USEC and BEAR have separate starting items and skill levels. The starting setup is committed once before perk grants.
3. Create personal/common perks from supported effect templates, set parameters and descriptions, and configure points and conflicts. Unsupported imported effects stay visible and unavailable.
4. Define 1–8 document types. Choose a compatible installed item or clone it into campaign-owned content. Configure dimensions and stack limits under Items and crates. The maximum is eight per raid; defaults are eight per raid, thirty per 23-hour window, and a five percent Classified chance.
5. Arrange battle-pass tiles on the 2×3 grid, and seasonal rewards on the 5×2 grid. Drag tiles to move them; edit width and height to resize. Each tile can contain several item, customization, trader-unlock, or local Tarcoin payloads. Set document costs, faction, level and completed-quest requirements. Previous-page requirements count enabled tiles.
6. Create native loot-container items and configure weighted pools, number of rolls, and found-in-raid behavior. Select the exchange crate, or leave it blank to disable that exchange.
7. Create quest chains with Level, Quest, TraderLoyalty, FindItem and HandoverItem conditions. Item objectives use ordinary inventory items. Installed quests can also be chosen directly in reward requirements. Quest objectives, messages, item rewards, XP, skills and trader rewards use SPT’s native contracts.
8. Upload PNG artwork (8 MB maximum, at most 4096×4096), or reuse available images. Edit English text or add translations. Empty/missing translations fall back to the authoritative English fields. Language codes must match the installed game’s locales for the translation to appear in-game.
9. **Save changes**, then **Validate**. Issues link to the relevant section or reward. Simulate level, faction, completed quests, claimed tiles, document balances and perk combinations; preview never reads or changes a player profile.
10. **Publish pack** creates an immutable revision. Download its ZIP to share it. Restart SPT to load new packs, then choose the campaign when creating a campaign character. Each character has its own campaign; there is no global activation step in the Creator.

Missing installed dependencies allow publication/export with a dependency report, but prevent play on that server. Missing artwork, unsupported enabled behavior and malformed structure block publication. Owned model assets are referenced from installed content; campaign packs contain no executable code or Unity bundles.

## Storage and recovery

All private authoring files live beside the server mod under `creator/`, outside `wwwroot`:

- `legacy.json`: the first imported bundled campaign, including existing configuration.
- `drafts/`: saved drafts and their backups, including archived and trashed drafts.
- `packs/`: immutable published revisions, definitions, owned PNGs and checksum manifests.
- `assets/`: uploaded artwork, addressed by content hash.
- `used/`: gameplay hashes of campaigns with characters.
- `selection.json`: legacy default-pack selection and its last loading error, retained for compatibility. The creator no longer changes this selection.

Preserve these files and SPT’s profiles/profile data together. Do not remove archived packs that a character uses. Pack downloads contain only manifest-listed definition/artwork files; they never include profiles, credentials or unrelated mod files. ZIP import checks paths, duplicate entries, file sizes, identities and checksums. PNG uploads check bounds and chunk integrity.

A legacy default-pack loading failure falls back to the last valid default and shows a loading issue in the library. Draft corruption falls back to its atomic backup when available. Competing editor tabs must reload after a save conflict. Used campaigns permit presentation revisions; gameplay edits require **Duplicate as new campaign**. Duplication remaps owned content and internal references while preserving installed dependencies.

Multiple characters can share a campaign, and all compatible published campaigns can be played during the same server session. Quests, gameplay and rewards follow the selected character's campaign. See [character selection and wipe behavior](characters.md).

## Story extension

Format-1 Story definitions survive draft, pack import/export and duplication. The graphical editor includes chapters, quest membership, notes, conversations, variables, entry points, raid bindings and media references, plus an isolated story rehearsal. Start **Your first story quest** from Help and tutorials for a complete example. The [story composition tool and example overlay](story-authoring.md) remain available for external authoring. Unity media is installed separately from the Creator ZIP and verified against authored SHA-256 hashes. [Story capabilities and limits](story-system.md) apply.


### Connected quest workflow and reference names

Installed traders use their localized nickname, with a full-name fallback. Item, quest, offer and craft references show names across selectors, previews and rehearsal facts. Searching never replaces the selected name with its ID; expand **Reference ID** when you need the underlying identity. Unknown references remain visible for repair. Draft names take precedence over installed copies.

**Chapters and quests** is the story authoring workspace. Select a chapter, open **Quests**, and use **Create quest in this chapter** or select an existing quest. Creation, duplication and editing stay within that chapter. Use the quest's **Details**, **Objectives**, **Rewards** and **Story** tabs; use **Chapter settings** for its title, visibility and artwork. The Story tab contains status notes and linked conversations. **Create and link note** creates the note and status association together; **Create quest conversation** creates the phase variable and entry point as well.

**Non-story quests** is separate and lists quests without a valid chapter assignment. It has no Story behavior tab. To convert a non-story quest, use **Add to a story chapter**, or add it from the chapter's **Add an existing non-story quest** control. Moving a quest preserves its identity, objectives and rewards. Imported broken memberships remain accessible here with a repair notice.

Quest links and validation links open the quest inside its chapter. Duplicating a chapter quest retains its membership; deleting an unreferenced quest removes its own membership too. Other references continue to block deletion.

Conversation editors include **Conversation availability and phase**, so entry conditions and the phase variable can be edited alongside the dialogue. The **Back to** button returns to the previous editor section and selected story/quest record. All changes still require **Save changes**, invalidate previous validation, and require restarting an active rehearsal.


Deleting a chapter opens a review of its quests and journal notes. Choose **Move quests and notes to another chapter** to preserve them, or explicitly confirm **Delete chapter and listed contents**. The chapter's own memberships do not block this operation. References from other quests, conversations or other content still block removal and provide links to the affected records. Cancelling or encountering a blocked reference leaves the draft unchanged; successful deletion still requires **Save changes**.


## Field groups and contextual help

Native quest objectives separate player instructions, targets and amounts, accepted item quality, combat restrictions, and location or timing. Counter filters are nested within their parent objective and do not repeat player instruction controls. Prerequisite quest statuses allow multiple accepted states. Quest rewards are grouped by acceptance, completion and failure; new rewards are added to the selected stage. Advanced quest settings omit fields already covered by the main tabs.

Story conditions and actions group behavior, targets, comparisons, progression and presentation. Help reflects the current condition type, comparison, value and selected variable declaration. Native field help distinguishes finding from handing over items, distance in metres from counts, reputation from experience, durability bounds and single-raid counters. Perk help explains the effect being changed and whether increasing or decreasing its multiplier is beneficial. Reward placement help uses the current grid dimensions; crate weights show the current share of the pool per draw.

---

[Documentation home](Home.md) · [Guide navigation](_Sidebar.md)
