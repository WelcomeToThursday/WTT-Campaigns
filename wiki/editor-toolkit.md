# Editor UI Toolkit

The Campaign Editor uses Unity UI Toolkit throughout: home, campaign-test controls, browsers, inspectors, tool windows, search, scope dropdowns, menus, conflicts and route/AI overlays. The old uGUI Editor controls and fallback have been removed. EFT's screen manager still handles native navigation, environment and input ownership.

All workspaces share one style sheet and reusable controls. Hierarchical browsers use virtualized Toolkit lists, while the Scene catalog uses responsive thumbnail pages and a bounded session cache (remote catalog requests still use ten-record batches). Window positions and sizes retain the existing saved layout format. Route authoring remains a separate tool.

## Console

Open **Windows → Console** in the in-game editor. The console can float, resize, or dock beside other tools; its placement and visibility are saved with your workspace. It starts hidden in new and older saved layouts.

Editor action feedback goes directly to the console, including selection guidance, validation warnings and operation errors. The former Notice pop-up and its show/hide/dismiss controls have been removed. These messages are retained while the console is hidden and appear under the default Campaigns filter.

The default filter shows Campaigns messages and command output. Enable **All client logs** to include game and other mod messages. Search and severity filters apply to logs; command results always remain visible. Messages wrap in the scrollable output area. Use the mouse wheel or scrollbar to read older output; scrolling back pauses **Auto-scroll**. Enable it again to follow new messages. Select a message to copy it, or double-click it to expand its details. On narrow windows, scroll the toolbar horizontally to reach its controls.

Rows show aligned timestamps to the second; message details and copied text retain milliseconds. Info is blue, Debug is muted purple, Warnings are amber, and Errors are coral red. Each row also has a matching severity stripe and a written level label.

Use **A− / A+** to adjust console text from 10 to 24, or **Reset** to restore 16. This setting is saved separately from the overall editor UI size and applies to output, expanded details and text fields. Changing it keeps your messages and command history. The window title and toolbar retain their normal size; use the separate overall editor UI-size setting to resize those controls.

Logs and command history last for the editor session. The console captures messages even when hidden, retains up to 2,000 messages and 2 MiB of text, and reports how many older messages were discarded. Individual messages longer than 16,384 characters are truncated. Clear only empties the console; it does not change log files. Server log streaming is not included.

Press **Enter** to run a command, **Tab** to cycle completions, and **Up/Down** to recall the last 100 commands. Names are case-insensitive; quote arguments containing spaces. Escape dismisses suggestions first, then releases input focus.

| Command | Action |
| --- | --- |
| `help [command]` | Show commands or detailed usage. |
| `clear` | Clear the console output. |
| `status` | Show the map, draft, active tool and synchronization state. |
| `tool Scene` | Switch to an available tool. Type `tool ` and press Tab for choices. |
| `window "Editor controls" show` | Show, hide or toggle a window. Type `window ` and press Tab for names. |
| `selection` | Describe the current selection and its available transform. |
| `focus` | Frame a selected visible scene object in the Scene workspace. |
| `camera speed [value]` | Read or set camera speed from 0.25 to 96 metres per second. |
| `undo` / `redo` | Use the editor's existing edit history. |

Commands respect active-session, conflict, preview and selection restrictions. Normal editor synchronization handles changes made through commands.

### Manual navigation tools

Open **Windows → Navigation** in a dedicated editor session. Native navigation stays registered. The full-map replacement workflow and Waypoints registration takeover have been removed. Waypoints remains required and loads its own navigation normally.

- **Add:** drag over a physical floor, ramp or platform. Visible one-metre cells define the permitted footprint. Surrounding space is never filled, and existing navigation is excluded from additions.
- **Block:** paint navigation cuts on the selected physical floor, affecting existing or new navigation. These do not create physical walls. Native agent clearance can widen the carved boundary beyond the painted footprint.
- **Erase Edits:** erase Add or Block cells on that floor. Erasing a block allows underlying navigation to return on the next preview. Other floors are retained.
- **Brush radius:** 0.75–8 metres. **Lock to first painted floor** limits strokes to ±0.6 m of the first height; turn it off for ramps. Walls occlude painting. Escape exits the brush.
- **Connect:** click two supported endpoints within 5 metres. These are explicit short ground-level walks; none are generated automatically. Building checks endpoint navigation, clearance, continuous physical support and a two-way physical path. Jumping, vaulting, ladders and stair traversal links are unsupported. Undo or **Erase Connections** removes saved connections.
- **Check Path:** click start and destination to check the active navigation. Painted overlays alone are not active paths.

Each completed brush stroke is one undo step. Ctrl+Z/Y also restores connections and erased cuts. **Build Preview** recovers collision geometry within the painted region, clips actual mesh/box/terrain geometry to Add footprints, excludes native navigation and asynchronously builds additions. Empty paint cannot trigger a build. Results extending beyond the saved footprint or bridging unpainted holes are rejected. Block-only layouts require no mesh recovery. Additions inherit the native infantry profile.

**Clear Preview** removes only owned additions, connections and blocks; saved paint stays. Clear before editing or rebuilding. Cuts settle over a few frames. Cancellation waits for native bake/readback cleanup. Layout edits, undo and scenery changes invalidate the preview without automatically rebuilding it. Editor closure releases preview resources. Physical upper faces explicitly painted as support are protected from their own authored-object box cuts; the solid below remains carved. Physics colliders are unchanged.

**All floors / This floor** captures active navigation in blue. Green shows Add paint, brighter green shows baked additions, red shows Block footprints and yellow shows explicit connections. With **Show through geometry** enabled (the default), navigation is drawn over the finished editor image, bypassing terrain depth, world culling and native scene effects. Disable it for normal terrain/wall occlusion; use **This floor** to isolate stacked levels. Empty areas with no navigation triangles stay empty. Refresh the snapshot after changing the preview. **Hide** hides navigation overlays.

Paint footprints and the brush outline follow sampled physical ground contours. Footprints use a cached quarter-metre display grid within each saved one-metre cell; this does not change saved paint or bake geometry. Unsupported portions and abrupt ledges are omitted and counted in the paint status rather than drawn as flat floating squares.

**Navigation Checks → Scan Nearby** scans active navigation within 24 m of the ground beneath the camera, using the current floor filter. It checks physical support, standing clearance, two-way navigation connectivity to that origin, slopes above the native agent limit, narrow physical passages, and mismatches between navigation height and physical support. Use the individual filters to inspect overlapping issues. Red indicates support or clearance findings, amber indicates slope/width/height warnings, and purple indicates no two-way navigation route to the origin (which may be intentional). Cyan means no issue at the sampled locations, not guaranteed coverage or bot acceptance.

**Project onto ground** displays navigation as a footprint projected onto visible terrain and floors using scene depth. This avoids floating triangle sheets and ground hiding the overlay. Projection stays within each captured triangle and within two metres of its height; real footprint holes remain empty. Terrain coverage must match the supporting terrain height. Mesh-floor coverage uses the native navigation voxel, step and slope settings to accommodate the bake's floor offset and its smoothing of stair treads into ramps. Sloping navigation gains bounded upward step reach; flat navigation does not gain that extra reach onto furniture. Walls, risers and separate storeys remain excluded. At every camera distance, terrain matching allows its native screen-space detail error (capped at 35 centimetres), plus the uncertainty of the scene depth buffer. The extra terrain allowance requires the visible surface to agree with the terrain slope, so it does not simply widen the contact band for leaves. This keeps distant coverage visible when the rendered terrain simplifies; it does not add an overlay distance cutoff. This rejects raised grass, foliage and unrelated prop tops rather than treating every upward-facing surface as ground. Steep walls, sky and surfaces outside the selected floor band are excluded. Disable projection to inspect the raw triangles. This is a display of nearby navigation coverage, not a clearance or path guarantee. It never expands or modifies navigation.

Checks run in batches across frames and stop at 4096 sampled triangles. The panel reports when the limit leaves coverage incomplete. Ordinary blue navigation outside the scanned region remains unchecked; grey hatching marks unverified or stale diagnostic samples. Layout, preview and floor changes invalidate results; rescan after moving doors or other dynamic geometry. **Clear Checks** cancels the scan and removes its overlay. Results use a dedicated shader with cached per-triangle flags. Ground projection reads scene depth and draws into the native camera frame before transparent objects and image effects. Terrain and coverage then share the same post-processing, orientation and viewport scaling, keeping coverage attached as the camera moves. It uses a cached footprint mesh and GPU projection, without per-frame navigation sampling or a second camera. Raw-triangle mode retains normal depth testing. Performance and visual acceptance still require in-game measurement. These checks never edit, expand or repair navigation.

Use **Observe** to test authored bots against the active preview. Observation retains prepared scenery and checks ownership and geometry revisions before spawning and while running. End observation before clearing or editing navigation. Clear the preview before player playtests, walkthroughs or mission rehearsals.

Paint uses navigation recipe version 2 in content format 13, with up to 4,096 cells and 128 connections inside a 128 × 128 × 64 metre region per layout. One recipe is allowed per composed map. Version 1 settings recipes still load, but never infer painted areas or trigger replacement. Saved data contains authoring inputs, not native baked binaries. This release supports **editor previews**; mission/ordinary-raid activation is not enabled. Tactical cover and AI zones are unchanged.

**Show source diagnostics** exposes Scan Paint, selectable issues and Save Report. Recovery permits four concurrent GPU readbacks, 128 meshes, a 64 MiB reservation and 8 MiB per input buffer. Unsupported primitives, unreadable collision-only meshes and unresolved terrain-tree coverage fail explicitly instead of producing guessed walkable support. Smaller painted regions can avoid unrelated unsupported sources.

Console commands: **navmesh build**, **scan**, **status**, **issues [page]**, **select number**, **cancel**, **restore**, **view [all/floor]**, **hide**, **report**. Build uses saved paint only; restore clears owned preview edits. Old recover, bake, inspect and apply commands are rejected. Reports go to `BepInEx/config/WTT-Campaigns/navigation-diagnostics/`.

Live acceptance: paint a platform and ramp, connect a ground-level entrance, block a doorway, test paths and bot movement, then clear/erase/undo. Check stacked floors, terrain, deliberate islands, cancellation, reopening and two consecutive observations. Offline checks do not certify live Unity behavior.

## Validation after installation

Manually restart the game client after installing. If server assemblies also changed, manually restart the server before reconnecting.

1. Open Editor home, select an existing mission or level (or create one), then enter its map. Check returning home and the disposable campaign test's reset/return controls.
2. Check Layouts, Routes, Zones, Events, Captures, Scene, AI, Hazards, Terrain and Navigation: search, tree expansion, paging, selection, creation and deletion, property edits and undo/redo. Verify scope changes refresh their dependent lists.
3. Inspect original scenery before editing; inspection must not create a saved change. Verify previews, placement, restore and rebind, including native-object restrictions.
4. Type in fields while pressing movement keys. The camera must stay still. Escape releases focus before cancelling a tool or closing. Changing selection must not apply unfinished text to another record.
5. Move, resize, overlap, hide and reopen windows; check tooltips, dropdowns and conflict dialogs. Clicking or scrolling controls must not manipulate the world behind them.
6. Check route and AI markers while moving the camera, walkthrough mode, and repeated map unload/reload. Check 1080p and higher display scales.
7. Open Console, dock it, resize it narrowly and restore its layout. Test filters, expanded details, copy, auto-scroll and heavy log output. Check quoted window names, history, Tab completion and the two-stage Escape behavior. Typing commands must not move the camera or activate scene shortcuts. Try undo/redo with empty history and focus with no selection; both should explain why the action is unavailable.

Offline validation checks installed Unity API compatibility, absence of uGUI Editor references, control inventories, native lifecycle contracts and asset hashes. Live rendering, input and performance still require in-game acceptance. No performance gain is claimed without measuring frame timing and memory on the same workload.
