# WTT-Campaigns 0.9.0 — Unreleased

The 0.9.0 development cycle is underway.

## Maintenance

- Organized Campaign Editor client code into focused view, scene, rendering, and preview namespaces. This is an internal code cleanup with no intended gameplay changes.

---

# WTT-Campaigns 0.8.0 — Campaign Editor and missions

This beta adds an in-game workspace for building campaign maps, authored AI encounters and patrols, and playable missions linked to campaign quests.

## Preview

[Watch the 0.8.0 preview on YouTube](https://www.youtube.com/watch?v=wcW27lK7Ai4)

## New and improved

- **Campaign Editor.** Enter from the main menu or choose Editor as your startup preference. Work with a disposable editor character, open a draft and map layout, and return to your original character when finished.
- **Map and scene authoring.** Move, rotate, resize, duplicate or hide supported scenery; configure doors and barriers; place loot and weapon presets; and build ordered checkpoint routes. Preview changes without rewriting the game's map bundles. Saved layouts support undo/redo, draft recovery, conflict handling and explicit rebinding of changed scene targets.
- **Editor workspace.** Browse scene objects, routes, zones and AI in searchable trees, edit selections in the properties panel, and arrange resizable panels. Selection highlights and transform controls help position objects directly in the map.
- **AI encounters and patrols.** Configure triggers, ordered waves, bot rosters, spawn points and patrol routes. Patrols support waypoint waits, walking or running, and loop, ping-pong or stop-at-end behavior. Placement and paths are checked against the map's existing navigation mesh. Authored patrols yield to SAIN combat behavior.
- **Observe and playtest.** Watch encounters from the free camera or rehearse with copied equipment on disposable editor state. Reset previews and retry without transferring test loot, damage or progress to your original character. This release also addresses repeated playtest startup and cleanup.
- **Playable missions.** Link an authored layout, briefing and story quest. Accept the quest, deploy from Missions, complete checkpoints in order and reach the authored exit. Missions use authored enemies and normal character equipment; quest rewards are handled by normal turn-in and are not repeated on replay.
- **Mission testing.** Test a saved mission directly from the editor, or use a disposable campaign flow to exercise quest acceptance, mission deployment, extraction, turn-in and replay. Campaign format 7 supports mission content while retaining support for older campaign formats.
- **Editor stability and performance.** Improvements cover map loading and exit recovery, scene selection, terrain visibility, idle synchronization and memory handling, and small-frame rendering during display transitions.

## Requirements and updating

Targets **SPT 4.1.x / EFT 0.16.9.40743**. Install these dependencies separately:

- **UnityToolkit 2.0.2 or later**, including its prepatcher.
- **WTT-CommonLib 3.0.6 or later**, with matching client and server components.
- **WTT-ContentBackport 2.0.1 or later**, with its dependencies.
- For AI encounters and patrol previews, the integration targets **SPT 4.1.5** with compatible **BigBrain 1.5.0** and **SAIN 4.5.1**.

Close the game and server and back up profiles before updating. Extract the archive's `BepInEx` and `SPT_Runtime` folders into your SPT installation. Install the full matching 0.8.0 package, including the editor UI bundle. Preserve configuration, regular and campaign profiles, and the server mod's entire `creator` folder. Start the server and game manually when ready.

## Limitations

- This is a beta authoring release; it does not include a complete authored story campaign. Local test drafts described in the guides are not promised as bundled campaign content.
- Fika is not supported. AI integration and real map behavior still require in-game verification with your installed mods.
- Unsupported scene objects remain read-only. Missing or ambiguous scene targets require rebinding. The editor does not rebuild navigation meshes, and invalid spawn or patrol paths cannot run.
- Ordinary mission runs use your real campaign character and normal raid consequences. Use the disposable testing paths for rehearsal.
- Offline contracts, native assembly checks and package hashes do not establish live gameplay acceptance.

## Guides

- [Installation and overview](https://github.com/WelcomeToThursday/WTT-Campaigns/blob/V0.8.0/README.md)
- [Campaign Editor](https://github.com/WelcomeToThursday/WTT-Campaigns/blob/V0.8.0/wiki/editor-mode.md)
- [AI encounters and patrols](https://github.com/WelcomeToThursday/WTT-Campaigns/blob/V0.8.0/wiki/ai-encounters.md)
- [Missions and disposable testing](https://github.com/WelcomeToThursday/WTT-Campaigns/blob/V0.8.0/wiki/missions.md)
