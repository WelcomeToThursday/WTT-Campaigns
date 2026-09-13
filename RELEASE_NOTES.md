# WTT-Campaigns 0.8.0 — Editor milestone

- Added Campaign Editor entry and a separate Normal / Editor startup preference.
- Added disposable editor sessions, restricted native map loading, editor home and direct map exit.
- Extended the existing raid editor bundle with map layouts, scenery overrides, door previews, barriers and ordered walkthrough routes.
- Added reversible scene previews, target rebinding, format 4 map data, draft recovery and concurrent editing support.
- Existing ordinary-raid authoring remains opt-in. Dedicated playable missions, encounters and rewards are a later milestone.

See the [Editor mode guide](wiki/editor-mode.md) for controls and the user-controlled acceptance pass. Offline validation does not replace that in-game pass.

# WTT-Campaigns 0.7.0

Beta release expanding campaign authoring, trader assortments and character customization, with progression fixes since [0.6.1](https://github.com/CJ-SPT/SeasonalPerks/releases/tag/V0.6.1).

## New and improved

- **Campaign trader assortments.** Choose an installed trader and edit, add, remove or replace their offers for a campaign. Configure item assemblies, currency or barter payments, loyalty requirements, stock and per-player purchase limits. Offers can use normal trader access or require a Battle Pass or campaign reward unlock. Stock and purchase limits follow the trader's native restock schedule. Clear and restore actions include a review and undo.
- **Standalone trader editor.** Open `/wtt-campaigns/creator/traders` to edit an assortment without a campaign draft. Save an independent workspace and export a native `assort.json`, preserving retained offer and child IDs. Exporting does not install the file or change live traders.
- **Client-rendered item previews.** Enable **Web item previews → Enable item authoring** in the game's settings, stay at the main menu and select that client in the web editor. Preview assembled items and installed modded content using the game's item images. Cached images remain available offline; campaign publication requires successful local client verification of authored assemblies. The preview worker creates detached items without changing inventory or making purchases.
- **Guided quest and dialogue editing.** Navigate chapters and quests in a searchable tree, edit objectives and rewards in dedicated steps, and create linked journal notes and conversations. Conversation writing, conditions, effects and presentation have separate controls. Deleting chapters or conversations shows affected content and reference checks before applying changes.
- **Connected raid authoring.** Improve in-raid editing and synchronization through SPT's existing notification WebSocket, with named references, draft recovery and conflict handling. Install matching client and server components.
- **Salvage quest zones.** Author salvage interactions for quests using WTT-CommonLib. This feature requires its matching client and server components.
- **Character appearance selection.** Add the backported customization screen to character creation.
- **Documentation in the web interface.** Read the player and author guides from the web interface, including expanded dialogue and creator workflows.

## Progression fixes

- Add Peacekeeper's **Demonstration Model** after auditing missing quests for compatibility. Unsupported objectives, unverified zones, incomplete rewards and dependent quests remain excluded.
- Rebalance completion reputation so the seven regular traders have offline-verified routes to maximum loyalty without event quests, Arena, repeatables or edition bonuses. **Supplier** now awards **+0.50 Ragman reputation** to resolve the early Ragman progression bottleneck. Native level requirements and prerequisite chains still apply.
- Older regular and campaign characters receive the positive reputation difference for affected completed quests once per character. A verified full profile backup is required before adjustment; a saved receipt prevents duplicate credits. Quest progress and other rewards are preserved.

## Requirements and updating

Targets **SPT 4.1.x / EFT 0.16.9.40743**. Install these dependencies separately:

- **UnityToolkit 2.0.2 or later**, including its prepatcher.
- **WTT-CommonLib 3.0.6 or later**, with matching client and server components.
- **WTT-ContentBackport 2.0.1 or later**, with its dependencies.

Close the game and server, back up your profiles, then extract the archive's `BepInEx` and `SPT_Runtime` folders into your SPT installation. Use the full matching package. Preserve configuration, regular and campaign profiles, and the server mod's entire `creator` folder, including standalone assortment drafts. Manually start the server and game when ready.

## Known limitations

- Fika is not supported.
- Fence is not supported by the assortment editor. Ref and Fence retain their existing reputation progression.
- Standalone exports contain `assort.json` only. Update your trader mod's external quest-assortment or code references if you remove referenced offers.
- Imported authored assemblies must be verified on the receiving author's installation before republishing. Missing client content can prevent verification.
- The bundled campaign does not include Kord Breach quests or a complete authored story campaign; some artwork remains placeholder content.

See the [README](https://github.com/CJ-SPT/SeasonalPerks/blob/V0.7.0/README.md), [creator guide](https://github.com/CJ-SPT/SeasonalPerks/blob/V0.7.0/wiki/season-creator.md) and [compatibility guide](https://github.com/CJ-SPT/SeasonalPerks/blob/V0.7.0/wiki/compatibility.md).
