# Mission Editor and Level Editor

Campaign Editor provides a restricted workspace for authoring the spatial part of a mission. Detailed story, quest and item forms remain in the web Creator. AI encounter authoring and previews are covered in [AI encounters and patrols](ai-encounters.md). See [Missions](missions.md) for playable mission raids, quest unlocks, replays, and disposable testing.

## Enter the workspace

From the normal main menu, choose **Editor**. The name and tools automatically change to **Mission Editor** for independent or legacy mission-owned layouts, and **Level Editor** for ordinary-raid layouts. The enablement checkbox does not determine the editor mode. Pending character operations finish before the client reconnects to a disposable editor character. Entry is unavailable during a raid.

For future launches, set **Campaign editor → Startup mode** to **Editor** in the client configuration, or use the startup button on editor home. Continue launching through the usual SPT launcher. The default is **Normal**. **Return to game** restores the character selected before entry and does not change this preference.

Editor home has separate **Missions** and **Levels** tabs. Missions selects mission content and layouts; campaign missions remain mission-only. Choose **New Mission**, enter a name and select a map to create and open an independent mission draft. Creation does not publish the mission or enable standalone play. Levels lists individual ordinary-raid layouts directly, without choosing a campaign. Existing ordinary layouts retain their source identities and enablement settings. Use **New Level**, enter its name, choose a map, then **Create & Open Level**. This creates a separate backing draft using existing storage; it does not publish or enable the level. Mission and campaign tests appear only on Missions, with campaign testing requiring story context. **Refresh levels** reloads the level library. **Esc** closes a choice list first, or returns to your game character from home.

## Web editors

Use the **Campaigns**, **Missions**, and **Levels** navigation in Creator. The Level library lists individual ordinary layouts and creates new levels from a name and map. Its editor shows only the selected layout and its owned zones, without mission AI or checkpoint controls. Mission layouts cannot be opened by a Level editor link. The in-game **Open Creator** button links directly to the selected level. Legacy shared drafts retain all other content; publishing a shared draft still publishes that entire saved draft.

## Paint terrain and grass

Open **Terrain** from the tool rail or Windows menu, then point at exposed terrain to load that tile's native palette. Select a texture thumbnail and **Paint**, or switch to **Grass** and choose **Add**, **Remove selected**, or **Clear all**. **Restore original** brushes blend back toward the map's original texture weights or grass density. Texture paint changes the native footstep and impact surface; it does not automatically remove grass.

Adjust radius (0.5-20 metres), strength, and falloff; grass also has a target density. Drag the left mouse button to paint. Each stroke stays on its starting tile and creates one undo step. **Escape** cancels an unfinished stroke; **Ctrl+Z/Y** undo/redo. Buildings and other solid surfaces block terrain selection. Grass rebuilds after release for both the main view and scopes; wait for the progress message to finish before painting again. **Remove layout paint** removes all saved terrain strokes from the current layout and supports undo.

Recipes travel with the layout through draft synchronization, recovery, mission packages, and enabled ordinary-raid map layers. Terrain content uses format 14 and needs matching client/server components. Missing or changed terrain bindings are reported; the editor does not substitute another tile. Edited texture tiles use Unity's terrain LOD renderer instead of EFT's baked proxy mesh so distant paint remains visible. Native palette availability and memory limits are shown in the panel. Terrain height, trees, mesh floors, imported textures, and new terrain creation are outside this tool.

## Build a route

The workspace includes **Layouts**, **Routes**, **Scene**, **AI**, **Zones**, **Hazards**, **Events** and **Captures**. Library and properties panels can pop out and dock again. Changing modules preserves the panel arrangement and selected map layout.

Hierarchical browsers share an expandable tree: **Routes** groups ordered markers under their layouts, **AI** groups encounters, waves, rosters, and patrol waypoints, and **Zones** separates Shared zones from the selected layout's zones. Use a branch arrow to expand or collapse it and select a record's name to open its properties. Search retains the parent context of matching records; clearing it restores your branch state. Trees scroll continuously without pages. Selecting a route marker also selects its owning layout.

1. In **Layouts**, create a layout. Name it in the properties panel.
2. Open **Routes**. Place one player start, add numbered checkpoints in route order, and place an exit. Markers use the floor directly beneath the editor camera (within 20 metres), facing the camera's horizontal direction. Fly closer to a floor if placement is unavailable. **Under camera** moves an existing marker there. Use numeric transforms or the Move / Rotate tools to adjust them after capture.
3. Open **Scene** and add barriers to close unwanted passages. Barriers have translucent editing previews and become invisible solid blockers during walkthrough. Resize barriers and volumes with their size fields or the Scale tool. Checkpoints and exit volumes support box and sphere shapes. **Earlier** and **Later** reorder checkpoints.
4. In **Scene**, use the scene picker to select a supported prop. Choose **Move prop**, **Copy prop** or **Hide prop**, or use the Scene catalog and contextual controls below. Independent static props can be resized. A captured native door cycles through unchanged, open, closed and locked.
5. Use snapping, numeric transforms and undo/redo to refine the layout. Remove an override to restore its original behavior. **Rebind** explicitly replaces a missing or changed scenery target with the currently picked object.
6. Use the existing draft save controls. Incomplete routes can be saved; walkthrough requires a start, at least one checkpoint and an exit.

## Zone scope

Zones can be **Shared** or belong to a specific layout. Existing zones remain Shared. Choose Shared to author a zone independently of layouts, or the current layout to keep it with that layout. The Zones library shows shared zones alongside zones for the selected layout; switching layouts hides zones belonging to other layouts. You can change a zone's scope later, including returning it to Shared. Creator exposes the same choice in the zone properties.

Duplicating a layout copies its zones with new identities. Deleting a layout removes its owned zones only when they are no longer referenced; reassign quest or story references first. Shared zones are unaffected. Enabled ordinary-raid levels activate their own zones and hazards. Publishing quest/story references to level zones requires an enabled level; Shared zones retain their existing campaign behavior. Level Editor creates new zones on the selected layout.

## Dress the scene

Open **Scene** after selecting a layout in **Layouts**. The library has three tabs:

- **Catalog** provides **All game / Current map** sources and **Props / Containers / Loot / Presets** categories. All game includes the exported level scenery library; Current map limits results to discovered map content. Search names or source paths, page through thumbnails, and turn off **Hide unavailable** to inspect restrictions. See the [game-wide catalog](raid-authoring.md#game-wide-scene-catalog).
- **In scene** lists original supported props, loose loot, searchable containers, and your placed objects. Use **Pick scenery** or click an object in the viewport to select its supported root.
- **Changes** lists saved placements and overrides, including removed objects. Removed originals stay accessible here for restoration or rebinding.

Select a catalog entry and press **Place**. Point at a surface and click once to place the object; **Escape** cancels. The placement becomes selected. Use **Move**, **Rotate**, axis handles, or numeric transforms to refine it. Snapping uses 5 cm translation and 5-degree rotation; hold **Alt** to bypass it. Independent static props, including originals, can be resized. Native loot and containers keep their original size.

For an existing object, choose **Move** or **Rotate** to create its layout override. Changes appear immediately. **Remove** hides an original object, but deletes an authored placement. **Restore original** removes an override and returns the original position and visibility. Undo/redo covers placement, completed drags, removal and restoration.

Searchable map containers retain their native identity and contents. Loot placements use actual installed item models held at the authored position; editor gameplay, looting, and profile progression remain disabled. Published, enabled level layouts apply in ordinary raids; mission-owned layouts stay exclusive to missions. See [Map layers](map-layers.md).

Changes use the existing draft synchronization, local recovery and conflict workflow. Save/reopen and pack import/export preserve placements. Container IDs and loose-loot spawn identities are checked when reopening; randomized loot that is absent or changed is reported rather than replaced with a similar item. Select the unresolved change, choose **Rebind to picked**, then click a replacement of the same type. Resolve or remove invalid records before walkthrough.

Scene roots, players, corpses, quest machinery, special interactions, combined static geometry and unsupported components remain unavailable. The exported scenery and native container libraries support cross-map placement of eligible objects without loading their source map. Source map bundles are never rewritten. Leaving a layout restores original objects and releases its editor-owned models; volume markers retain translucent overlays.

Opening an editor map keeps the native loading progress and starts its elapsed timer afresh for each load. Once loading finishes, the editor skips the raid Deploy screen and countdown while retaining normal world and audio initialization.

## Camera speed and player routes

The viewport toolbar's **Fly m/s** field sets camera speed from **0.25 to 96 metres per second**. Use **− / +** to halve or double it. The default is 6 m/s and your choice is remembered. Hold RMB to fly; **Shift** gives a 4x boost and **Ctrl** gives quarter-speed precision. Diagonal movement keeps the same speed.

**Routes** is the walking-figure tool. Select a layout to work on its player start, ordered checkpoints and exit. Create and manage layouts in **Layouts**; barriers and scenery tools are in **Scene**.

1. Fly above a floor and choose **Set start**. Markers use the floor within 20 metres beneath the camera and its heading.
2. Choose **Add checkpoint** along the route. Selecting an existing checkpoint inserts the next one immediately after it; otherwise new checkpoints append.
3. Select a waypoint and use **Frame waypoint** to bring the camera to it. Use handles or numeric fields to adjust its transform and volume; **Under camera** places it again. **Earlier / Later checkpoint** changes traversal order. Duplicates insert after the selected checkpoint.
4. Choose **Set exit**. The Routes inspector holds **Walk from marker**. Walkthrough becomes available when the layout has valid start, checkpoint and exit records; captured scene targets and standing clearance are also checked when starting.

Existing layout routes remain available in Routes without conversion. A green **START** flag, amber numbered checkpoint diamonds, and a red **END** square identify the route even before you select a waypoint. Checkpoint and exit volume previews use matching colors. Bright outlined connections follow checkpoint order; the markers, labels, and connections remain readable through walls and terrain while Routes is open. A white outline marks the selected waypoint without changing its role color. These authoring overlays disappear during walkthrough, when the status names the next checkpoint, then directs you to the exit, then reports completion.

## Walk through and leave

**Walkthrough** validates the entire layout and resolves every captured target before applying physical changes. **Walk from marker** is on by default, so the player moves to the start; the marker needs a clear standing space and a floor. Turn it off only to begin at the current player body position. Markers saved before the camera-placement fix keep their stored positions; select each misplaced marker and use **Under camera**, or place it again.

Layout editing is frozen while walking. Checkpoints advance in their authored order, followed by the exit. This progress is preview-only. Press **Esc** to restore the scene and return to editing at the editor camera's previous position and direction. **Reset preview** also ends the preview. If the original player position is clear, a relocated player returns there. The editor camera bookmark applies only to the current raid.

**Unload map** returns to editor home through native scene cleanup without extraction or raid results. The selected draft remains available. Open another map or use **Return to game**.

Editor maps disable normal input actions, damage, survival drain, stamina consumption, AI spawning and raid completion. Normal gameplay HUD and results screens are suppressed. Walking, free-camera editing and editor-controlled door state previews remain available. These restrictions apply only while the editor session is active.

The editor keeps SPT's existing notification connection active during authoring raids so drafts can stay synchronized. If that connection drops, the workspace remains open and shows the connection status while retrying; local draft edits remain recoverable. A walkthrough stops after an extended connection interruption and restores the original scenery.

## Time, weather and map visibility

Open **Session → Environment / time and weather** in the map editor. Enter a time from **00:00** to **23:59** and choose **Set time**, use Dawn/Noon/Dusk/Night, or step by one hour. A selected time stays held for lighting previews; **Use raid time** restores the current raid clock.

Weather has Clear, Cloudy, Rain and Storm presets. Enter percentages for clouds, rain, fog, wind and thunder, then choose **Apply weather**. The wind-direction button cycles through eight compass directions. **Use raid weather** returns to the current native weather. Maps without a sky clock or weather controller show those controls as unavailable.

Time, weather and wind direction are saved as local editor preferences and reapplied whenever the editor opens, including after loading another map or restarting the client. **Use raid time** and **Use raid weather** clear their saved overrides independently. Closing the editor, entering walkthrough, or unloading the map restores native conditions; the raid timer continues. Configure mission conditions separately under **Time of day and weather** in the server web mission editor. Visibility sampling follows the free camera, including EFT's cached map-culling observer. Unity occlusion culling stays disabled on the editor camera while editing, including after native visibility switches update. Terrain hidden by player-triggered zones is drawn during editing, with its terrain/LOD visibility restored on close or walkthrough. Dedicated editor flight also bypasses baked visibility-cell rejection and baked renderer hiding, so flying outside normal player areas does not inherit an empty visibility set. Player-triggered scene switches are also suspended during dedicated editor flight: their registered components, LOD groups and objects are revealed, including components that ignore inverse zones. Pending hide operations are stopped before editing begins. Native distance and LOD selection remain active; authored hidden objects stay hidden. Closing or entering walkthrough restores captured trigger states, requests native trigger reevaluation and refreshes baked visibility queues. Each scene discovery logs the number of baked groups, trigger targets and disabled targets revealed for troubleshooting. The player is not moved by these corrections.

Live check: fly from the spawn area through distant buildings and above the map; verify scenery follows the camera. Try time and weather presets, reset each independently, enter a walkthrough, and load another map. Check that normal weather, lighting and visibility return afterward.

## Editor windows

The native editor home groups draft selection and map settings in a compact framed workspace. In a loaded map, each authoring tool and Properties has a movable, resizable window. Windows can dock as tabs or split groups around the scene viewport; Environment, Help and Console open from **Windows**. Actions sit beside their relevant records, and secondary technical information is under Details. Positions and sizes are remembered between launches. See [Arrange the workspace](raid-authoring.md#arrange-the-workspace) for controls and layout reset.

Editor action messages, warnings and errors are retained in **Windows → Console**, replacing the former Notice panel. The console supports search, severity filters, adjustable text and editor commands. See the [console guide](editor-toolkit.md#console). For viewport maximize, overlays, orbit and pan controls, see [Arrange the workspace](raid-authoring.md#arrange-the-workspace).

## Drafts and isolation

The server creates the editor character from a clean native template, with inventory roots and the native empty pockets container for traversal, and no carried gameplay items. It is not linked as an account or campaign character and is excluded from launcher profile lists. Native scratch saves are discarded; a separate editor storage path also prevents scratch files entering gameplay profile storage. Ending a session removes its scratch profile. Sessions expire after one minute without a heartbeat; normal requests then retire abandoned editor state. After a client crash, wait for this lease to expire before retrying a Normal startup. Expired sessions are replaced on editor retry, and abandoned scratch files are cleaned on server startup. Drafts are retained separately.

Map layouts use campaign format **4**; loot placements and native loot/container overrides use format **5**; AI encounters, spawn points and patrol routes use format **6**; playable mission definitions use format **7**. Later formats add further authoring features, including independent missions and campaign links in format **11**. Earlier formats remain supported; see [Missions](missions.md#existing-embedded-missions). Version 0.11.0 adds splines in format **12**, navigation recipes in **13**, and terrain painting in **14**; dedicated-editor requests use authoring protocol **7**. Matching client and server components are required for map editing. The authoring service preserves layouts when older authoring clients submit other spatial edits, and rejects older map-editor submissions that could discard newer records. Existing draft conflict handling, local recovery, campaign duplication and pack import/export include layouts and missions. Incoming changes wait until walkthrough or testing finishes.

Source map bundles are never rewritten. Targets use map, scene, hierarchy and structural fingerprint; native door IDs are included. Missing, ambiguous or changed targets require explicit rebinding. AI placement and patrols are validated against active navigation. [Manual navigation previews](editor-toolkit.md#manual-navigation-tools) can add bounded areas, cuts and short connections in the dedicated editor while retaining native navigation; those recipes do not activate in deployed missions or ordinary raids.

## User-controlled acceptance

After installing matching components, manually restart the server and client. Development validation never launches them.

- Enter from a normal character, then repeat with the Editor startup preference.
- Select a draft and map. Move or hide supported scenery, duplicate a prop, adjust a door and barrier, and build a route through a building.
- Leave a loaded editor map idle for two minutes after indexing completes. Check that RAM settles and there is no regular one-second stutter, then edit, undo/redo and save. Idle draft checks reuse committed change state, and unchanged synchronization polls do not rebuild the workspace.
- Editor maps keep automatic garbage collection enabled, including during walkthrough. On leaving the map, the latest collection mode requested by the game is restored. Terrain is discovered when the workspace opens or a scene loads; native visibility changes are still corrected before camera rendering. No periodic forced collection or working-set trimming is used.
- Place a prop, loot template and weapon preset. Move/remove a loose item and a searchable container; verify undo, restore, cancellation, and unchanged container contents.
- Save, unload and reopen the layout. Check its transforms, route order and target resolution.
- Start walkthrough with and without relocation. Confirm ordered checkpoints, collision, disabled gameplay and restoration on exit.
- Move and resize Browser and Properties, switch categories, run a walkthrough and open a second map. Restart the client manually and verify the saved layout returns. Check Windows / Reset layout and both panels at 1280×720.
- Return to game and verify the original character, inventory, progression and normal gameplay. Review the normal profile files for unexpected changes.
- Test a connection interruption and failed map load; retry or return from recovery. Confirm saved drafts survive.

Offline contracts, native assembly checks and Unity previews cover implementation behavior and compatibility. Actual map geometry, native transitions and other installed mods still need this in-game acceptance pass.

**Campaign editor → Memory diagnostics** is off by default. Enable it when troubleshooting to record a bounded capture in `BepInEx/LogOutput.log`. Load a map and leave it open for three minutes after the workspace appears. Lines beginning `Editor memory` report process memory, managed heap use, Unity and graphics memory, available native object counters, frame stalls and editor subsystem timings. Subsystem totals include nested calls and must not be added together. Capturing stops automatically after three minutes; it does not force garbage collection, change its mode, enumerate scene objects or enable the global profiler. Disable the setting when diagnostics are no longer needed.

Editor mode bypasses native Bloom, BloomAndFlares and Prism effects when a frame is too small for their downsampling buffers. This prevents zero-sized render-texture exceptions and skipped temporary-buffer cleanup during startup or display transitions. Normal-sized frames retain the native effects, and normal gameplay rendering is unchanged. The client logs the first undersized frame and camera-object name once for diagnosis; persistent blank or tiny rendering still needs an in-game check.

Click an object in the Scene workspace to select it, highlight its bounds, and open its properties. Trigger volumes do not block clicks; collision-only children resolve to their visible parent, and scenery without colliders can be selected by its bounds. Selection does not create a draft edit. The first changed property or completed drag creates the edit; Escape restores a cancelled drag. Rotate uses colored rings, and Resize uses the object’s colored axes. Objects with unsupported gameplay components or combined static meshes remain selectable for inspection, with the restriction shown above read-only properties.

Live acceptance: click an original prop without capturing it first, rotate/resize it, cancel a second drag, and undo/redo the first edit. Check a collider-free placed item, a prop behind a trigger, an LOD prop, and a restricted object. Each click should expose the matching properties without changing the draft.

## Hazard areas

Use the **warning-triangle icon** in the left tool rail to open **Hazards**. Choose **+ Minefield**, **+ Claymore**, **+ Sniper zone**, or **+ Barbed wire** to place an area at the aim point. Select its record to name, move, rotate, resize, duplicate or delete it. The red box shows coverage; a claymore also shows a forward direction line. **New: Shared** makes a hazard available in ordinary campaign raids on that map; **New: Layout** limits it to the selected layout. Enabled ordinary-raid levels activate their owned hazards; mission layouts activate theirs during mission runs.

Hazards stay inert during authoring, Observe and Walkthrough. **Playtest** arms the selected layout's hazards and its Shared hazards. Active mission raids also use these hazards. Reset removes the runtime instances, cancels pending sniper shots, clears authored wire slowdown and restores unspent claymores for the next test.

- **Minefield:** native landmine damage on entry, followed by further explosions as the player moves through the area.
- **Claymore:** one directional explosion per run. The box is its activation area, and the mine sits at its rear edge facing the direction line. Its blast can extend beyond the activation box.
- **Sniper zone:** native border fire, beginning with a warning shot and becoming lethal after sustained exposure. Leaving the box cancels pending shots and their sounds. **Play shot sound: On** uses a native rifle report; **Suppressed shots: On** uses the native suppressed rifle report. Turn **Play shot sound: Off (silent)** to keep completely silent zone fire. Existing zones default to normal shots.
- **Barbed wire:** native limb contact damage and movement slowdown with BSG's native razor-wire coils, material and contact sounds, available across maps. Overlapping authored wire areas retain slowdown until the last area is left.

The initial tool uses fixed native damage settings, a simple claymore marker and generated wire geometry. Wire contact audio reuses a loaded native wire sound bank when the map supplies one. All four use box volumes and remain separate from quest trigger types.
