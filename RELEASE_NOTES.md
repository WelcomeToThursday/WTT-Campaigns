# WTT-Campaigns 0.11.0 — Scenery, terrain and mission authoring

Changes since **0.10.1**. This release expands the scenery library, adds terrain painting and editable route curves, and improves mission creation, presentation and playtesting.

## Scene catalog and editor workspace

- **Expanded level scenery library.** Browse exported scenery from the installed game's level files, including supported inactive objects. Shared meshes and textures reduce duplication; source orientation, LOD groups and supported colliders are preserved. Selecting a prop loads its resources without opening another map.
- **More useful catalog results.** Search names, alternate names, source levels and asset paths. **Hide unavailable** filters unsupported entries, with reasons available when the filter is disabled. Responsive thumbnail pages use the available panel space, and a bounded preview cache reuses rendering resources.
- **Improved selection and layouts.** Picking scenery updates its inspector without changing the current browser tab. Selection outlines respect source model orientation. Editor layouts, toolbars, inspectors and action groups have received spacing and usability improvements.
- **Create missions in game.** Choose **New Mission** from editor home to create and open a mission draft. Mission and Level workspaces retain their separate tools and content.

## Terrain and navigation tools

- **Paint native terrain textures and grass.** Choose a tile's texture palette, paint with adjustable radius, strength and falloff, or add/remove native grass. Restore brushes blend back toward the original map. Strokes support undo/redo, and recipes travel with mission packages and enabled ordinary-raid layouts. Texture painting also changes the native footstep and impact surface.
- **Manual navigation previews.** Paint Add and Block footprints, erase edits, and create explicit short ground connections. Build Preview creates bounded additions while retaining native navigation. Check Path and nearby diagnostics help inspect support, clearance and connectivity; floor filters and ground-projected overlays help inspect stacked areas.
- **Navigation remains an editor preview feature.** Observe can test authored bots against an active preview. Clear it before player playtests, walkthroughs or mission rehearsals. Saved navigation recipes do not activate in deployed missions or ordinary raids. Terrain painting does not change terrain height or add new terrain.

## Route curves and authored AI

- **Editable splines for patrols and player routes.** Edit points and handles, insert points without changing the curve shape, and smooth selected corners or entire routes. Corner, Auto, Aligned and Free handle modes support precise shaping, with snapping and undo/redo.
- **Grounded patrol curves.** Patrol curves follow connected ground while preserving their horizontal shape. Invalid ground, blocked connections and insufficient standing clearance are marked on the curve with the affected waypoint pair. Preview and mission activation reject invalid patrol curves; player-route curves remain visual guides.
- **Better patrol tools and continuity.** Insert, reorder and reverse waypoints, inspect directed paths and distances, and repair invalid waypoint drafts. Patrols preserve targets, travel direction and remaining waits across combat interruptions and leader changes. Improvements to clearance, squad spacing and bounded retries reduce repeated planning and help squads resume their authored routes.

## Mission settings and presentation

- **Mission timers.** Choose the map default, a timed mission from 1–1440 minutes, or Infinite. The mission banner and objective rows remain visible in playtests and deployed missions. Checkpoint retries restore the saved remaining time; the countdown pauses during checkpoint operations and the retry menu.
- **Time of day and weather.** Set a mission start time, optionally hold it fixed, and configure clouds, rain, fog, wind, thunder and wind direction. Settings are included in published revisions, exports and playtests. Checkpoint retries restore the saved world time, and leaving a playtest restores the underlying raid environment. Local editor preview preferences are saved separately.
- **Objective and checkpoint notifications.** Choose default and per-objective icons from native quest sprites or custom PNG artwork. Custom mission artwork is included in exports.
- **Full mission playtests from the editor.** Playtest runs the selected layout's mission with objectives, events, timer and checkpoint notifications. Layouts without a mission retain the AI-only playtest; Observe remains an AI preview.

## Requirements and updating

Targets **SPT 4.1.x / EFT 0.16.9.40743**, with AI integration targeting **SPT 4.1.x**. **Waypoints 1.9.0+ is now required.** Other dependencies are **UnityToolkit 2.0.2+** with its prepatcher, **WTT-CommonLib 3.0.6+**, **WTT-ContentBackport 2.0.1+** and its dependencies, **BigBrain 1.5.0+**, **SAIN 4.5.1+**, **MoreBotsAPI 2.1.1+**, and **Black Division 1.3.1+**. Install dependencies separately.

Close the game and server and back up profiles before updating. Extract `BepInEx` and `SPT_Runtime` into your SPT installation and install the **full matching 0.11.0 package**, including the expanded scenery library and UI bundles. Preserve configuration, profiles and the server mod's entire `creator` folder. Restart the server and client manually.

Older supported content remains loadable; use matching updated components to author or load the new features. The Map Variants mod (`com.lennoxp90.mapvariants`) is now declared incompatible.

The full wiki is embedded in the server for Creator's Documentation pages and included as readable files in the archive. Offline checks cover contracts, component compatibility and assets; live rendering, input, terrain presentation and third-party AI behavior still require in-game verification. Fika remains unsupported, and this beta does not include a complete authored story campaign.

## Guides

- [Installation and overview](https://github.com/WelcomeToThursday/WTT-Campaigns/blob/V0.11.0/README.md)
- [Terrain painting and editor workflows](https://github.com/WelcomeToThursday/WTT-Campaigns/blob/V0.11.0/wiki/editor-mode.md)
- [Navigation previews](https://github.com/WelcomeToThursday/WTT-Campaigns/blob/V0.11.0/wiki/editor-toolkit.md#manual-navigation-tools)
- [Splines and patrols](https://github.com/WelcomeToThursday/WTT-Campaigns/blob/V0.11.0/wiki/ai-encounters.md#spline-paths-and-smoother-corners)
- [Mission settings and playtests](https://github.com/WelcomeToThursday/WTT-Campaigns/blob/V0.11.0/wiki/missions.md)

[Full changes since 0.10.1](https://github.com/WelcomeToThursday/WTT-Campaigns/compare/V0.10.1...V0.11.0)

---

# WTT-Campaigns 0.10.1 — Editor console, viewport and AI recovery

Changes since **0.10.0**. This update adds an in-game editor console, improves the scene viewport and camera controls, and adds workload limits and recovery handling for authored AI encounters.

## Editor console and feedback

- **Dockable console.** Open **Windows → Console** to view Campaigns messages, or enable **All client logs** for game and other mod output. Search, severity filters, message details, copy and auto-scroll are included. Window placement and visibility are saved.
- **Editor commands.** Use `help`, `clear`, `status`, `tool`, `window`, `selection`, `focus`, `camera speed`, `undo` and `redo`. Command entry supports quoted arguments, Tab completion and Up/Down history, and respects editor session, conflict and preview restrictions.
- **Readable output.** Wrapped messages scroll independently of command entry. Info, Debug, Warning and Error have distinct colors and edge markers. Timestamps align to the second; details and copied text retain milliseconds. A− / A+ / Reset controls save console text size separately from editor UI scaling, without enlarging the title or toolbar. Scrolling back pauses auto-scroll.
- **Console replaces notices.** Editor action feedback, validation warnings and operation errors now go directly to the console. The old Notice pop-up and show/hide/dismiss controls have been removed. Feedback is retained while the console is hidden; UI refreshes do not republish it.
- **Bounded logging.** Console history is limited to 2,000 entries and 2 MiB of text per editor session; long individual messages are truncated and discarded-entry counts are shown. Server log streaming and scripting are not included.

## Viewport and camera

- The game view now occupies the editor's central viewport and resizes with dock dividers. Picking, placement, handles and route markers use the same viewport coordinates; floating tools block clicks through them. It presents the existing camera output without rendering a second scene.
- Added viewport camera-speed controls, snapping, saved overlay choices, clean view and Maximize / Restore. Use **Shift+Space** over the viewport to maximize/restore and **G** to toggle clean view. Escape restores the viewport before closing the editor.
- Added **Alt+left-drag** orbit and **middle-drag** pan, alongside right-mouse free flight. Improved pointer capture/restoration and navigation behavior around editor controls. Walkthrough and playtest restore fullscreen game presentation.

## Authored AI and mission recovery

- Added configurable authored-AI budgets: defaults are **64 active bots**, **one spawning wave** and **eight navigation checks per frame**. Ready waves wait for capacity, oversized waves are rejected before spawning, and native actor activation is limited to once per frame. Ordinary raid spawning and SAIN combat are unchanged by these budgets.
- Patrol planning distributes work across frames and squads. Deferred navigation work is distinguished from an unreachable route.
- Recognized transient profile-request failures retry up to three total attempts. Partial-wave activation rolls back registered actors and AI bindings, and spawn observations are published only after the whole wave activates.
- Unrecoverable mission encounter failures pause the attempt as a technical interruption, separately from player defeat. When enabled, checkpoint retry restores the saved checkpoint with a fresh attempt identity. Ending an interrupted attempt uses the native alive exit path without granting mission completion; acknowledgement failures keep the attempt paused.

## Compatibility and maintenance

- Reorganized editor controllers and migrated shared collection queries to ZLinq, with offline compatibility checks against the client 1.5.3 and server 1.5.6 libraries. Shared does not ship its own ZLinq copy.
- Added offline coverage for viewport input, AI budgets and recovery, console commands, bounded concurrent logging, feedback delivery and listener cleanup.
- Updated the wiki for console controls, viewport navigation, mission interruption recovery and dependencies. The full wiki is embedded in the server for Creator’s Documentation pages and included as readable files in the archive.

## Updating and validation

Targets **SPT 4.1.x / EFT 0.16.9.40743**, with AI integration targeting **SPT 4.1.5**. Dependencies remain **UnityToolkit 2.0.2+** with its prepatcher, **WTT-CommonLib 3.0.6+**, **WTT-ContentBackport 2.0.1+** and its dependencies, **BigBrain 1.5.0+**, **SAIN 4.5.1+**, **MoreBotsAPI 2.1.1+**, and **Black Division 1.3.1+**. Install dependencies separately.

Close the game and server and back up profiles before updating. Extract `BepInEx` and `SPT_Runtime` into your SPT installation and install the **full matching 0.10.1 package**, including UI bundles and asset libraries. Preserve configuration, profiles and the server mod's entire `creator` folder. Restart the server and client manually. There is no content-format or authoring-protocol bump in this patch.

Offline validation covers contracts, installed API compatibility and asset hashes. Live Unity presentation, input, checkpoint recovery and third-party AI behavior still require in-game verification. Fika remains unsupported; this beta does not include a complete authored story campaign.

## Guides

- [Installation and overview](https://github.com/WelcomeToThursday/WTT-Campaigns/blob/V0.10.1/README.md)
- [Editor console](https://github.com/WelcomeToThursday/WTT-Campaigns/blob/V0.10.1/wiki/editor-toolkit.md#console)
- [Viewport and authoring](https://github.com/WelcomeToThursday/WTT-Campaigns/blob/V0.10.1/wiki/raid-authoring.md)
- [AI budgets and recovery](https://github.com/WelcomeToThursday/WTT-Campaigns/blob/V0.10.1/wiki/ai-encounters.md#encounter-failure-recovery)

[Full changes since 0.10.0](https://github.com/WelcomeToThursday/WTT-Campaigns/compare/V0.10.0...V0.10.1)

---

# WTT-Campaigns 0.10.0 — Missions, levels and editor improvements

This beta separates playable missions from ordinary-raid levels, adds reusable mission publishing, and expands the in-game and web authoring tools.

## New and improved

- **Independent missions.** Create and publish missions with their own briefing, map layout, objectives, encounters and loot. Campaigns can link a specific published revision with their own story unlock and optional quest-completion binding. Existing campaign-owned missions remain supported.
- **Dedicated Level Editor.** Editor home and the web Creator now have separate Missions and Levels libraries. Create a level from a name and map, then open its focused workspace. Mission-owned layouts retain mission tools; ordinary levels expose the tools relevant to normal raids.
- **Levels in ordinary raids.** Published, enabled layouts can add scenery, doors, barriers, loot, containers, layout-owned zones, hazards and additional extracts. Regular characters choose layers from MAP LAYERS; campaign characters use their campaign defaults. Mission layouts are excluded from ordinary-raid layers. Native player spawns and normal AI remain in control.
- **Mission objectives and checkpoints.** Expanded mission event and AI objective authoring, encounter brain choices, and checkpoint restoration handling. Disposable tests support rehearsing mission content without transferring results to the original character.
- **More usable editor workspace.** Compact, resizable panels, improved docking and toolbars, searchable choices, numeric editing, collapsible inspectors and saved layout preferences. Editor presentation now uses Unity-authored templates and styles.
- **Scene, doors and loot.** Improved scene selection and outlines, deliberate placement and repeat placement, native prop movement, placed-door recovery, container map identities and generated ammunition positions.
- **Zones, hazards and character transitions.** Layout-scoped zones and native hazard authoring, improved preview equipment loading, and a profile-save gate that lets pending character operations finish before entering disposable editor state.

## Requirements and updating

Targets **SPT 4.1.x / EFT 0.16.9.40743**, with AI integration targeting **SPT 4.1.5**. Install dependencies separately: **UnityToolkit 2.0.2+** with its prepatcher, **WTT-CommonLib 3.0.6+**, **WTT-ContentBackport 2.0.1+** and its dependencies, **BigBrain 1.5.0+**, **SAIN 4.5.1+**, **MoreBotsAPI 2.1.1+**, and **Black Division 1.3.1+**. See the README for details.

Close the game and server and back up profiles before updating. Extract the archive's `BepInEx` and `SPT_Runtime` folders into your SPT installation. Install the **full matching 0.10.0 package**, including UI bundles, AI integration and asset libraries. Preserve configuration, regular and campaign profiles, and the server mod's entire `creator` folder. Start the server and game manually when ready.

Independent mission packages and campaign links use **content format 11**. Older content remains supported. Update client and server together. New levels are saved as drafts; publish and enable them before expecting changes in ordinary raids. Publishing a legacy shared draft publishes that entire draft.

## Beta limitations

- Fika is not supported. This release does not include a complete authored story campaign.
- Offline validation checks contracts, assemblies and assets; live Unity presentation, checkpoint retries, AI/SAIN behavior and consecutive-raid cleanup still require in-game verification.
- Ordinary missions use your actual character and normal raid consequences. Use disposable editor tests for rehearsal.
- Levels do not add mission AI, checkpoints, retries or custom player starts. Unsupported scene objects remain restricted, and the editor does not rebuild navigation meshes.
- Missing dependencies and unresolved scene targets must be corrected before running affected content.

## Guides

- [Installation and overview](https://github.com/WelcomeToThursday/WTT-Campaigns/blob/V0.10.0/README.md)
- [Mission Editor and Level Editor](https://github.com/WelcomeToThursday/WTT-Campaigns/blob/V0.10.0/wiki/editor-mode.md)
- [Independent missions and campaign links](https://github.com/WelcomeToThursday/WTT-Campaigns/blob/V0.10.0/wiki/missions.md)
- [Map layers in ordinary raids](https://github.com/WelcomeToThursday/WTT-Campaigns/blob/V0.10.0/wiki/map-layers.md)
- [AI encounters and patrols](https://github.com/WelcomeToThursday/WTT-Campaigns/blob/V0.10.0/wiki/ai-encounters.md)

---

# WTT-Campaigns 0.9.0 — Editor tools, containers and character recovery

This beta expands the Campaign Editor with a game-wide asset catalog, configurable native loot containers and a more flexible workspace. It also adds recovery for surviving campaign characters and updates the built-in KORD campaign copy.

## New and improved

- **Game-wide scene catalog.** Browse All game or Current map, with separate Props, Containers, Loot and Presets categories. Search installed asset names and bundle paths, preview supported objects and save cross-map asset placements. Discovery runs incrementally while the catalog is open and caches its results between sessions.
- **Native searchable containers.** Place supported containers with normal opening, searching and item transfer in walkthroughs and missions. The included native container library covers 45 templates, including weapon boxes, bags, jackets, safes and caches. Editing previews remain inert.
- **Loot configuration.** Give placed containers random native loot, fixed contents or no contents; select a loot pool, set spawn chance, and configure a key and lock. Contents and spawn results remain stable within a run, and a fresh run generates new results. Mission container state survives server reloads.
- **Flexible editor workspace.** Dock, resize and arrange tool windows, retain separate tool selections, and use the dedicated Loot configuration window. Improvements include catalog grids, tooltips, numeric dragging, UI scaling and panel separators. The editor remembers camera positions across map sessions.
- **Scene editing improvements.** More original props support movement and rotation while preserving their native effects and physics state. Selection from Maps opens the corresponding Scene inspector. Unsupported copying or resizing is explained in the inspector, including props with unreadable collision meshes.
- **Character recovery.** An administrator can link surviving campaign characters whose launcher account is missing to a replacement account. Recovery preserves inventory and campaign progress, keeps existing destination characters available, and backs up data before changing ownership. It cannot recreate deleted character saves.
- **Updated KORD campaign copies.** Fresh copies use the 12 released KORD BREACH quests with campaign-owned identities, adapted prerequisites, native camera placement and recovery, quest loot on Black Division operatives, shootable radio objectives, the Intelligence Center recipe and the Skier rifle offer. Existing drafts retain their edits; create a fresh copy to receive the updated template.
- **Compatibility and recovery fixes.** Optional AI code is isolated so missing SAIN or BigBrain no longer causes the startup type-loading failure reported in issue #10. This release also improves editor startup recovery, mission loot handling and client/server map-session compatibility.

## Requirements and updating

Targets **SPT 4.1.x / EFT 0.16.9.40743**, with AI integration targeting **SPT 4.1.5**. Install dependencies separately: **UnityToolkit 2.0.2+** with its prepatcher, **WTT-CommonLib 3.0.6+**, **WTT-ContentBackport 2.0.1+** and its dependencies, **BigBrain 1.5.0+**, **SAIN 4.5.1+**, **MoreBotsAPI 2.1.1+**, and **Black Division 1.3.1+**. Use matching client/server components where supplied. See the README for the full requirements.

Close the game and server and back up your profiles before updating. Extract the archive's `BepInEx` and `SPT_Runtime` folders into your SPT installation. Install the **full matching 0.9.0 package**, including its UI bundles, AI integration and container library. Preserve configuration, regular and campaign profiles, and the server mod's entire `creator` folder. Start the server and game manually when ready.

Configured containers use **campaign format 9 / authoring protocol 6**. Cross-map asset placements use format 8; older campaign content remains supported. Update client and server together before editing these layouts.

## Beta limitations

- Fika is not supported. Offline validation does not establish live Unity visuals, raid behavior or compatibility with every installed mod.
- Asset discovery does not make every object placeable. Unsupported linked gameplay components, missing resources and ambiguous scene targets still require resolution. The editor does not rebuild navigation meshes.
- Containers retain their original size. An editor connection retains up to 64 container-run receipts; reconnect if that limit is reached.
- KORD copies require the installed Black Division and ContentBackport content. Unreleased Historical Perspectives rewards and nine unavailable cosmetics remain disabled. Authored trader offers still require item-preview validation before publication.
- Normal missions use your actual campaign character and normal raid consequences. Use disposable editor tests for rehearsal.

## Guides

- [Installation and overview](https://github.com/WelcomeToThursday/WTT-Campaigns/blob/V0.9.0/README.md)
- [Campaign Editor](https://github.com/WelcomeToThursday/WTT-Campaigns/blob/V0.9.0/wiki/editor-mode.md)
- [Scene catalog and container configuration](https://github.com/WelcomeToThursday/WTT-Campaigns/blob/V0.9.0/wiki/raid-authoring.md#game-wide-scene-catalog)
- [Built-in KORD campaign copies](https://github.com/WelcomeToThursday/WTT-Campaigns/blob/V0.9.0/wiki/built-in-campaign-copy.md)
- [AI encounters and patrols](https://github.com/WelcomeToThursday/WTT-Campaigns/blob/V0.9.0/wiki/ai-encounters.md)
- [Missions and disposable testing](https://github.com/WelcomeToThursday/WTT-Campaigns/blob/V0.9.0/wiki/missions.md)

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
