# Campaign Creator

Open the **WTT-Campaigns creator link in the SPT launcher**, or visit [the Campaign Creator on your local server](https://127.0.0.1:6969/wtt-campaigns/creator) while SPT is running. If your server uses a different address or port, use the address from your launcher followed by `/wtt-campaigns/creator`. Sign in with an administrator account if prompted.

Use the Creator to make campaigns, configure starting characters and rewards, author quests and stories, and export packs for sharing. Install the complete matching client/server release before playing authored content.

## Workspace and help

The landing library keeps drafts and published packs in separate sections. Search by campaign name or draft description; draft cards show quest, perk and reward counts. Use **Create blank campaign** to start, or import a ZIP from the sidebar to open it as a draft. Published packs retain their revision, duplication and export actions. Import guidance and tutorials sit beside the library on wide screens and stack below it on smaller screens.

Grouped navigation stays at the left and a broad editing column holds the selected content. Help, connections and preview guidance live in a collapsed inspector below the workspace. Reward settings open directly below the reward grid. Each editor separates presentation, rules, content and advanced settings into labeled sections that stack on smaller screens.

Open **Help and tutorials** from the library or any draft for searchable instructions and a glossary. **Campaign basics** covers setup through export; **Your first story quest** walks through a chapter, native quest, completion note, conversation and rehearsal. Each editor section also includes **How to use this section**. Tutorials provide self-paced directions and navigation; they do not create or save content automatically. See the [tutorial guide](creator-tutorials.md) for a printable walkthrough and troubleshooting.

Hover or focus the **?** markers for explanations of point costs, collection windows, page gates, weighted crate contents and other settings. Tap a marker on touch devices; Escape dismisses focused help. Reward tiles show selection and disabled states. Select a tile to edit its contents and dimensions below the grid, then use **Save changes** in the top bar.

## Trader assortments

Open **Trader assortments** under **Rewards and economy** in a campaign draft. Choose a trader to see what they currently sell, then select **Edit assortment**. Vanilla and installed modded traders with fixed assortments are supported; Fence's generated inventory is not.

- **Edit offer** opens an existing offer's assembly, price, loyalty and stock settings. Editing an installed offer creates a campaign replacement and keeps its reward links connected.
- **Remove offer** removes it from this campaign's assortment.
- **Add offer** starts from an item, weapon preset or a copy of an installed offer. The trader is already selected.
- **Clear assortment…** removes all offers from this trader's campaign draft, so you can build it from empty.
- **Restore installed…** discards this trader's draft changes and restores its installed assortment.

Clear and restore actions show a review first and can be undone. Reward references must be removed before their offers can be deleted or the trader cleared. **Back to assortment** returns from an offer to the trader's offer list; **Choose another trader** returns to trader selection. **Finish editing** leaves edit mode; use the campaign's **Save changes** to persist the draft. Campaign changes take effect through normal validation and publishing, and affect only characters using that campaign. Installed trader files and other campaigns remain independent.

Select an item in the assembly tree, then select one of its attachment slots, ammunition containers, or storage grids. The item catalogue filters compatible templates for that destination. Use **Add to** or drag an item onto a slot or grid cell. Grid contents can be moved within the grid and rotated. Removing an attachment also removes its children. Empty required slots produce incomplete-assembly warnings; incompatible items, overlapping grid contents, and invalid quantities must be repaired. Use **Undo** and **Redo** to revise edits. On narrower screens, the **Catalogue**, **Assembly**, and **Offer settings** tabs switch between working areas.

In **Offer settings**, configure loyalty, stock, the per-player purchase limit, and currency or barter costs. A purchase requires every cost in its selected payment alternative. Zero purchase limit means unlimited purchases per player; finite stock and purchase limits follow that trader's native restock schedule. **Available in this campaign** uses ordinary trader access and loyalty gates. **Requires reward unlock** also requires an enabled Battle Pass or campaign reward with an **AssortmentUnlock** payload selecting this offer. Use **Edit campaign offer** in that reward to return to the assembly.

### Standalone editing and export

Use **Open standalone trader editor** in the Creator header, or open `/wtt-campaigns/creator/traders`. No campaign draft is needed. Choose a trader and select **Edit assortment** to copy their complete installed assortment into the standalone workspace. Edit, add, remove or clear offers using the same trader-first flow.

**Save draft** keeps your independent workspace. **Prepare export** saves and validates the selected trader, then offers **Download assort.json**. The download is a native SPT assortment with complete item trees, barter alternatives, loyalty levels, stock and purchase limits. Empty assortments can be exported too. Exporting does not install the file or modify live traders; use the file in the appropriate trader mod. Campaign reward settings are omitted from this workflow.

Existing offer and child IDs are preserved in standalone edits so external trader references keep working for retained offers. New or duplicated offers receive new IDs. The export contains only `assort.json`; if you remove offers referenced by your mod's `questassort.json` or code, update those references in that mod too.

Standalone drafts are stored under the mod's `creator/assorts/` directory with a backup of the previous save. Stale saves from another tab are rejected; reload the saved draft to resolve the conflict. Client images and assembly checks remain available, but standalone export performs structural and installed-template validation without requiring a campaign publication receipt.

### Client images and assembly verification

1. Install matching WTT-Campaigns client and server components and manually restart the applications.
2. In the game's WTT-Campaigns settings, enable **Web item previews → Enable item authoring** and remain at the main menu.
3. Expand **Item previews** and choose that client under **Client item images**. The selected assembly and current page of offers or catalogue items are rendered on demand using the same cached icon path as the game's trader screen, including installed modded items.
4. Wait for **Verified**, repair reported assembly errors, then **Save changes**, **Validate**, and **Publish pack**. Use **Refresh selected assembly** after repairing a missing bundle or stale image.

Images are cached on the server, so drafts remain editable after the client disconnects. Publication requires a successful client verification of each authored assembly. Changes to the assembly, relevant templates, or the selected client's mod/bundle fingerprint require another check; changes to prices and stock do not. Draft-only templates that the client has not loaded remain unverified until their content is installed and available. A disconnected client, pending render, or missing image does not prevent saving a draft.

The preview worker creates detached items and never changes a player inventory or performs a purchase. It pauses outside the main menu and shares the existing SPT notification connection with raid authoring. Each job is limited to 256 items, and previews to 1024 pixels and 1 MiB. The server keeps a bounded local cache in the mod's `user/item-preview-cache` folder; cached images and trusted verification receipts are not included in exported packs. A receiving author's installation must verify imported assemblies locally before republishing them.

Campaigns containing these offers use format 3 and need a release that supports trader authoring. Existing format-1 and format-2 packs remain supported. Offers can be updated in a used campaign. Publish an update under the same identity and restart SPT to load it.

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
10. **Publish pack** or **Publish update** creates a numbered release. Download its ZIP to share it. Restart SPT to load the latest valid release for new and existing characters. Each character has its own campaign; there is no global activation step in the Creator.

Missing installed dependencies allow publication/export with a dependency report, but prevent play on that server. Missing artwork, unsupported enabled behavior and malformed structure block publication. Owned model assets are referenced from installed content; campaign packs contain no executable code or Unity bundles.

## Storage and recovery

All private authoring files live beside the server mod under `creator/`, outside `wwwroot`:

- `legacy.json`: the first imported bundled campaign, including existing configuration.
- `drafts/`: saved drafts and their backups, including archived and trashed drafts.
- `packs/`: immutable published revisions, definitions, owned PNGs and checksum manifests.
- `assets/`: uploaded artwork, addressed by content hash.
- `used/`: historical usage hashes; these no longer lock gameplay editing.
- `selection.json`: legacy default-pack selection and its last loading error, retained for compatibility. The creator no longer changes this selection.

Preserve these files and SPT’s profiles/profile data together. Do not remove archived packs that a character uses. Pack downloads contain only manifest-listed definition/artwork files; they never include profiles, credentials or unrelated mod files. ZIP import checks paths, duplicate entries, file sizes, identities and checksums. PNG uploads check bounds and chunk integrity.

A rejected published update falls back to the previous valid release and reports a loading issue. Draft corruption falls back to its atomic backup when available. Competing editor tabs must reload after a save conflict. **Edit campaign** preserves campaign identity; **Publish update** makes a new numbered release for existing characters. **Duplicate as new campaign** creates a separate campaign by remapping owned content and internal references while preserving installed dependencies.

Multiple characters can share a campaign, and all compatible published campaigns can be played during the same server session. Quests, gameplay and rewards follow the selected character's campaign. See [character selection and wipe behavior](characters.md).

## Story extension

Format-1 Story definitions survive draft, pack import/export and duplication. The graphical editor includes chapters, quest membership, notes, conversations, variables, entry points, raid bindings and media references, plus an isolated story rehearsal. Start **Your first story quest** from Help and tutorials for a complete example. The [story composition tool and example overlay](story-authoring.md) remain available for external authoring. Unity media is installed separately from the Creator ZIP and verified against authored SHA-256 hashes; see [custom story media bundles](story-media-bundles.md) for custom-trader rooms and the other bundled media types. [Story capabilities and limits](story-system.md) apply.


### Connected quest workflow and reference names

The in-raid editor reuses the client's existing SPT notification WebSocket. It opens no additional connection or listener. Install matching client and server components, then manually restart both applications to use the updated connection. If the native connection is unavailable, the editor waits for SPT to reconnect it. Editor timeouts and raid cleanup do not close the shared socket. Local draft recovery, revision checks and conflict resolution still apply. Presence and draft checks keep their existing cadence. Each message is limited to 4 MiB by SPT's listener; an oversized update is reported in the editor.

Installed traders use their localized nickname, with a full-name fallback. Item, quest, offer and craft references show names across selectors, previews and rehearsal facts. Searching never replaces the selected name with its ID; expand **Reference ID** when you need the underlying identity. Unknown references remain visible for repair. Draft names take precedence over installed copies.

**Chapters and quests** lists each quest beneath its chapter in a searchable tree. Select a chapter name for its settings, or a quest name for **Basics**, **Unlock requirements**, **Objectives**, **Rewards**, **Story events** and **Preview**. A breadcrumb identifies the current chapter, quest and step. Quest steps are remembered when following links and returning during the editor session. Use **+ Create quest** beneath a chapter to add one.

Objective cards show player instructions and a separate summary of saved rules; expand **Edit rules** to edit. **Story events** shows when journal notes are revealed and links to related conversations. **Create and write note** assigns the chapter and quest status before opening the note workspace. **Create and write conversation** creates an introduction with a phase variable and entry point, then opens its dialogue outline. Select a line to edit **Write**, **Conditions**, **Effects** or **Presentation**. Guided continuation and ending controls preserve other effects; complex imported transitions stay editable in the condition and effect forms.

**Non-story quests** is separate and lists quests without a valid chapter assignment. It has no Story events step. To convert a non-story quest, use **Add to a story chapter**, or add it from the chapter's **Add an existing non-story quest** control. Moving a quest preserves its identity, objectives and rewards. Imported broken memberships remain accessible here with a repair notice.

Quest links and validation links open the quest inside its chapter. Duplicating a chapter quest retains its membership; deleting an unreferenced quest removes its own membership too. Other references continue to block deletion.

Conversation editors include **Conversation availability and phase**, so entry conditions and the phase variable can be edited alongside the dialogue. The **Back to** button returns to the previous editor section and selected story/quest record. All changes still require **Save changes**, invalidate previous validation, and require restarting an active rehearsal.


Deleting a chapter opens a review of its quests and journal notes. Choose **Move quests and notes to another chapter** to preserve them, or explicitly confirm **Delete chapter and listed contents**. The chapter's own memberships do not block this operation. References from other quests, conversations or other content still block removal and provide links to the affected records. Cancelling or encountering a blocked reference leaves the draft unchanged; successful deletion still requires **Save changes**.


## Field groups and contextual help

Native quest objectives separate player instructions, targets and amounts, accepted item quality, combat restrictions, and location or timing. Counter filters are nested within their parent objective and do not repeat player instruction controls. Prerequisite quest statuses allow multiple accepted states. Quest rewards are grouped by acceptance, completion and failure; new rewards are added to the selected stage. Advanced quest settings omit fields already covered by the main tabs.

Story conditions and actions group behavior, targets, comparisons, progression and presentation. Help reflects the current condition type, comparison, value and selected variable declaration. Native field help distinguishes finding from handing over items, distance in metres from counts, reputation from experience, durability bounds and single-raid counters. Perk help explains the effect being changed and whether increasing or decreasing its multiplier is beneficial. Reward placement help uses the current grid dimensions; crate weights show the current share of the pool per draw.

---

[Documentation home](Home.md) · [Guide navigation](_Sidebar.md)

To delete a conversation, select **Delete** and review the listed contents. Confirming removes its dialogue lines, its entry points and an unused Dialogue-scope phase variable. Quests and journal notes are kept, as are variables still used elsewhere. If another conversation, raid event or quest references the deleted content, the preview links to that record so you can remove or reassign the reference first. Cancel leaves the draft unchanged.

## Test saved campaigns in-game

Save your draft, then open the in-game campaign menu and choose **Test draft**. Select a saved campaign to start or continue its dedicated test character. Campaigns do not need missions, story content, or a map layout to be tested.

Test characters use separate saves per launcher account and draft under `user/seasonal/campaign-tests`. Returning to your character or editor preserves test progress, including across manual restarts. **Reset test character** asks for confirmation and starts fresh with the latest saved draft.

The test menu shows the loaded and latest saved revision. At the menu, choose **Apply saved changes** to refresh the tested campaign and retain compatible progress. Saving in Creator alone does not change a running test or an active raid. Missing dependencies must be installed first; changes to external asset bundles can require a manual restart.

**Edit campaign** resumes a working draft for a published campaign. **Publish update** preserves campaign identity and creates a new release without rewriting earlier exports. After a manual restart, existing characters use the latest valid release. Completed rewards, currency, inventory, and one-time grants remain intact. Changed unfinished objectives reset; unchanged objectives remain, and obsolete mission checkpoints are discarded. Removed quests retain their history so reintroducing them does not repeat previously granted rewards. Campaign update backups are stored under `user/seasonal/content-backups`.

Explicitly linked mission packages remain pinned to their chosen revision until the author updates the link. Test progress is never promoted into regular character progress when publishing.
