# Season Creator

Current workflow: [build, validate and always install](build-deployment.md). Never stop or start servers or clients. Any isolated-server results below are historical; those fixtures are retired.

The creator runs inside the installed SPT 4.1.3 Blazor host at `/wtt-campaigns/creator`. It uses the host’s interactive server rendering, MudBlazor layout, and `Administrator` policy. Install the full client/server package together; authored seasons require protocol 2. The isolated test installation is retired; use the installed server address when the user has it running.

## Workspace and help

The landing library keeps drafts and published packs in separate sections. Search by season name or draft description; draft cards show quest, perk and reward counts. Use **Create blank season** to start, or import a ZIP from the sidebar to open it as a draft. Published packs retain their revision, duplication and export actions. Import guidance and tutorials sit beside the library on wide screens and stack below it on smaller screens.

The creator inherits the SPT S.I.C. theme: shared dark surfaces, yellow accents, typography and control styling. Grouped navigation stays at the left and a broad editing column holds the selected content. Help, connections and preview guidance live in a collapsed inspector below the workspace. Reward settings open directly below the reward grid. Each editor separates presentation, rules, content and advanced settings into labeled sections that stack on smaller screens.

Open **Help and tutorials** from the library or any draft for searchable instructions and a glossary. **Season basics** covers setup through export; **Your first story quest** walks through a chapter, native quest, completion note, conversation and rehearsal. Each editor section also includes **How to use this section**. Tutorials provide self-paced directions and navigation; they do not create or save content automatically. See the [tutorial guide](creator-tutorials.md) for a printable walkthrough and troubleshooting.

Hover or focus the **?** markers for explanations of point costs, collection windows, page gates, weighted crate contents and other settings. Tap a marker on touch devices; Escape dismisses focused help. Reward tiles show selection and disabled states. Select a tile to edit its contents and dimensions below the grid, then use **Save changes** in the top bar.

## Manage drafts

The library starts with the most recently edited drafts. Use **Sort drafts** for oldest first or name order; existing drafts use their file date until their next edit. Dates are shown in the server's local time.

Open **Manage** on a draft to **Rename**, **Duplicate**, **Archive**, or **Move to Trash**. Rename changes the draft name only. Duplication opens an independent season with new content identities. Archive sets a draft aside; Trash removes it from your working list after a confirmation. Both views offer **Restore to Drafts**, preserve all content, and survive a server restart. Trash is never automatically emptied; there is no permanent-delete action.

Archived and trashed drafts must be restored before editing or publishing. Published packs and characters are unaffected by organization or removal of a draft. If another tab changed or moved a draft, save and management actions reject the stale revision; use **Refresh library** to see the current state.

## Create a season

1. Open the creator through SPT’s web interface with an administrator account. Select **Create blank season**, or use **Duplicate** on the draft or published pack you want to copy. Blank seasons use the bundled document model, neutral branding, valid artwork, one empty battle-pass page, and no selected perks.
2. Fill in Overview and Starting character. Choose an installed starter edition or leave it blank to use each account’s normal starter edition. Stash additions include money through the item picker; equipment replaces its selected slot. USEC and BEAR have separate starting items and skill levels. The starting setup is committed once before perk grants.
3. Create personal/common perks from supported effect templates, set parameters and descriptions, and configure points and conflicts. Unsupported imported effects stay visible and unavailable.
4. Define 1–8 document types. Choose a compatible installed item or clone it into season-owned content. Configure dimensions and stack limits under Items and crates. The raid cap is still eight; defaults are eight per raid, thirty per 23-hour window, and a five percent Classified chance.
5. Arrange battle-pass tiles on the 2×3 grid, and seasonal rewards on the 5×2 grid. Drag tiles to move them; edit width and height to resize. Each tile can contain several item, customization, trader-unlock, or local Tarcoin payloads. Set document costs, faction, level and completed-quest requirements. Previous-page requirements count enabled tiles.
6. Create native loot-container items and configure weighted pools, number of rolls, and found-in-raid behavior. Select the exchange crate, or leave it blank to disable that exchange.
7. Create quest chains with Level, Quest, TraderLoyalty, FindItem and HandoverItem conditions. Item objectives use ordinary inventory items. Installed quests can also be chosen directly in reward requirements. Quest objectives, messages, item rewards, XP, skills and trader rewards use SPT’s native contracts.
8. Upload PNG artwork (8 MB maximum, at most 4096×4096), or reuse available images. Edit English text or add translations. Empty/missing translations fall back to the authoritative English fields. Language codes must match the installed game’s locales for the translation to appear in-game.
9. **Save draft**, then **Validate**. Issues link to the relevant section or reward. Simulate level, faction, completed quests, claimed tiles, document balances and perk combinations; preview never reads or changes a player profile.
10. **Publish pack** creates an immutable revision. Download its ZIP to share it. Restart SPT to load new packs, then choose the season when creating a seasonal character. Each character has its own season; there is no global activation step in the creator.

Missing installed dependencies allow publication/export with a dependency report, but prevent play on that server. Missing artwork, unsupported enabled behavior and malformed structure block publication. Owned model assets are referenced from installed content; season packs contain no executable code or Unity bundles.

## Storage and recovery

All private authoring files live beside the server mod under `creator/`, outside `wwwroot`:

- `legacy.json`: the first imported bundled season, including existing configuration.
- `drafts/`: explicit saves with revision conflict checks and atomic replacement/backups. Organization status and last-edit timestamps live in each draft envelope; archive and Trash never move or delete its content file. Older envelopes remain readable without a migration write.
- `packs/`: immutable published revisions, definitions, owned PNGs and checksum manifests.
- `assets/`: uploaded artwork, addressed by content hash.
- `used/`: gameplay hashes of seasons with characters.
- `selection.json`: legacy default-pack selection and its last loading error, retained for compatibility. The creator no longer changes this selection.

Preserve these files and SPT’s profiles/profile data together. Do not remove archived packs that a character uses. Pack downloads contain only manifest-listed definition/artwork files; they never include profiles, credentials or unrelated mod files. ZIP import checks paths, duplicate entries, file sizes, identities and checksums. PNG uploads check bounds and chunk integrity.

A legacy default-pack loading failure falls back to the last valid default and shows a loading issue in the library. Draft corruption falls back to its atomic backup when available. Competing editor tabs must reload after a save conflict. Used seasons permit presentation revisions; gameplay edits require **Duplicate as new season**. Duplication remaps owned content and internal references while preserving installed dependencies.

Existing account links migrate lazily to a character list without replacing profiles. The original character belongs to the bundled legacy season. Multiple characters can share a season, and all compatible published seasons can be played during the same server session. The legacy configured pack remains a fallback for older callers; character creation offers the loaded seasons explicitly. Quests, gameplay and rewards resolve from the selected character's season. Inactive seasonal sessions cannot perform inventory mutations. See [character selection and wipe behavior](characters.md).

## Implementation map

- `Shared/Seasons`: authoring contracts, canonical compiler, dependency inventory, translation fallback and structural validation.
- `Server/Seasons`: atomic repository, immutable snapshots, early item registration, pack loading at server startup and starter setup transactions.
- `Server/Web`: administrator page/components, installed-content pickers, PNG upload, reward layout and simulation, authorized download/image endpoints.
- `Server/Profiles`: legacy link migration, per-season profile resolution and gameplay-hash tracking.
- `Client` and `UI`: protocol checks, document template mappings, season branding, localized text and season/revision asset identity.

## Verification

The implementation was exercised on synthetic profiles in `Testing/Server` only. The installed game/server profiles were not modified.

- 552 contract, storage and installed client-assembly assertions.
- 74 existing native profile/perk integration checks and 173 existing hub checks.
- 28 custom-season checks: starting items/skills, two-quest chain, gated rewards, native crate opening, document exchange, idempotent claims and protocol/season rejection.
- 179 custom-document raid checks: placement, pickup, split/merge, extraction, retries and collection limits.
- Season switching checks cover returning to the legacy character and restoring the custom character’s inventory and claims.
- HTTP checks confirm the creator page, local stylesheet and pack download are served by the SPT host. With authentication enabled, the editor, artwork and download endpoints require login.
- A corrupted pending pack fails checksum preflight and preserves the active pack and character state.

The server fixture described by earlier validation is retired from the workflow. Use the offline checks and mandatory installation in [build and deployment](build-deployment.md); never stop or start any server or client.

**Outstanding acceptance:** interactive browser saving, drag/drop, upload and simulation checks, and installed-game creation/branding/layout checks at supported resolutions. The local HTTPS certificate blocked the automated browser session. These gates must pass before treating 0.3.0 as a completed release.

## Story extension

Format-1 Story definitions survive draft, pack import/export and duplication. The graphical editor includes chapters, quest membership, notes, conversations, variables, entry points, raid bindings and media references, plus an isolated story rehearsal. Start **Your first story quest** from Help and tutorials for a complete example. The [story composition tool and synthetic overlay](story-authoring.md) remain available for external authoring. Unity media is installed separately from the Creator ZIP and verified against authored SHA-256 hashes. [Story compatibility and acceptance gates](story-system.md) apply.


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

Offline component rendering checks: run the EditorRendering project under tools with the project root as its argument. It renders actual Razor components into Research/EditorRendering and verifies key nested-objective, reward-stage and contextual-help behavior without connecting to a game or changing profiles. This does not replace interactive browser verification.
