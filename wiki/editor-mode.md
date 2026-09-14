# Campaign Editor and mission map layouts

Campaign Editor provides a restricted workspace for authoring the spatial part of a mission. Detailed story, quest and item forms remain in the web Creator. Playable mission raids, encounters, rewards and retries are a later milestone.

## Enter the workspace

From the normal main menu, choose **Campaign Editor**. Pending character operations finish before the client reconnects to a disposable editor character. Entry is unavailable during a raid.

For future launches, set **Campaign editor → Startup mode** to **Editor** in the client configuration, or use the startup button on editor home. Continue launching through the usual SPT launcher. The default is **Normal**. **Return to game** restores the character selected before entry and does not change this preference.

Editor startup opens directly into the full-screen **Campaign Editor** after authentication, using the native EFT menu environment and controls. It also returns here after unloading a map. Select a draft, layout or location from its scrollable choice list. Existing layouts keep their assigned location; choose **New layout in a map** to select another location, then **Open map**. **Refresh drafts** reloads draft choices. If there are no drafts, **Open Creator** to create one first. A failed connection leaves **Retry connection** and **Return to game** available. **Esc** closes a choice list first, or returns to your game character from home. The startup preference remains unchanged when you return.

## Build a route

The existing workspace now includes **Maps**, **Zones**, **Events**, **Captures** and **Scene**. Library and properties panels can pop out and dock again. Changing modules preserves the panel arrangement and selected map layout.

1. In **Maps**, create a layout. Name it in the properties panel.
2. Place one player start, add numbered checkpoints in route order, and place an exit. Markers use the floor directly beneath the editor camera (within 20 metres), facing the camera's horizontal direction. Fly closer to a floor if placement is unavailable. **Under camera** moves an existing marker there. Use numeric transforms or the Move / Rotate tools to adjust them after capture.
3. Add barriers to close unwanted passages. Resize barriers and volumes with their size fields or the Scale tool. Checkpoints and exit volumes support box and sphere shapes. **Earlier** and **Later** reorder checkpoints.
4. Use the existing scene picker to select a supported prop. Capture **Move**, **Copy** or **Hide**, or use the Scene catalog and contextual controls below. Independent static props can be resized. A captured native door cycles through unchanged, open, closed and locked.
5. Use snapping, numeric transforms and undo/redo to refine the layout. Remove an override to restore its original behavior. **Rebind** explicitly replaces a missing or changed scenery target with the currently picked object.
6. Use the existing draft save controls. Incomplete routes can be saved; walkthrough requires a start, at least one checkpoint and an exit.

## Dress the scene

Open **Scene** after selecting a layout in **Maps**. The library has three tabs:

- **Catalog** lists reusable **Props** from this map, installed **Loot** templates, and weapon **Presets**. Search by name (or item template ID for loot), page through results, and select an entry to see its thumbnail. Only supported independent props are included; unavailable installed item models report an error.
- **In scene** lists original supported props, loose loot, searchable containers, and your placed objects. Use **Pick scenery** or click an object in the viewport to select its supported root.
- **Changes** lists saved placements and overrides, including removed objects. Removed originals stay accessible here for restoration or rebinding.

Select a catalog entry and press **Place**. Point at a surface and click once to place the object; **Escape** cancels. The placement becomes selected. Use **Move**, **Rotate**, axis handles, or numeric transforms to refine it. Snapping uses 5 cm translation and 5-degree rotation; hold **Alt** to bypass it. Independent static props, including originals, can be resized. Native loot and containers keep their original size.

For an existing object, choose **Move** or **Rotate** to create its layout override. Changes appear immediately. **Remove** hides an original object, but deletes an authored placement. **Restore original** removes an override and returns the original position and visibility. Undo/redo covers placement, completed drags, removal and restoration.

Searchable map containers retain their native identity and contents. Loot placements use actual installed item models held at the authored position; editor gameplay, looting, and profile progression remain disabled. These layouts do not modify regular gameplay raids.

Changes use the existing draft synchronization, local recovery and conflict workflow. Save/reopen and pack import/export preserve placements. Container IDs and loose-loot spawn identities are checked when reopening; randomized loot that is absent or changed is reported rather than replaced with a similar item. Select the unresolved change, choose **Rebind to picked**, then click a replacement of the same type. Resolve or remove invalid records before walkthrough.

Scene roots, players, corpses, quest machinery, special interactions, combined static geometry and unsupported components remain unavailable. The prop catalog does not load assets from other maps. Source map bundles are never rewritten. Leaving a layout restores original objects and releases its editor-owned models; volume markers retain translucent overlays.

Opening an editor map keeps the native loading progress and starts its elapsed timer afresh for each load. Once loading finishes, the editor skips the raid Deploy screen and countdown while retaining normal world and audio initialization.

## Camera speed and player routes

The top toolbar's **Fly m/s** field sets camera speed from **0.25 to 96 metres per second**. Use **− / +** to halve or double it. The default is 6 m/s and your choice is remembered. Hold RMB to fly; **Shift** gives a 4x boost and **Ctrl** gives quarter-speed precision. Diagonal movement keeps the same speed.

**Routes** is the walking-figure tool beside Maps. Select a layout in its library to work on that layout's player start, ordered checkpoints and exit. Create layouts in **Maps**, which retains barriers and scenery records.

1. Fly above a floor and choose **Set start**. Markers use the floor within 20 metres beneath the camera and its heading.
2. Choose **Add checkpoint** along the route. Selecting an existing checkpoint inserts the next one immediately after it; otherwise new checkpoints append.
3. Select a waypoint and use **Frame waypoint** to bring the camera to it. Use handles or numeric fields to adjust its transform and volume; **Under camera** places it again. **Earlier / Later checkpoint** changes traversal order. Duplicates insert after the selected checkpoint.
4. Choose **Set exit**. The Routes inspector holds **Walk from marker**. Walkthrough becomes available when the layout has valid start, checkpoint and exit records; captured scene targets and standing clearance are also checked when starting.

Existing layout routes remain available in Routes without conversion. The connecting line follows checkpoint order. During walkthrough the status names the next checkpoint, then directs you to the exit, then reports completion.

## Walk through and leave

**Walkthrough** validates the entire layout and resolves every captured target before applying physical changes. **Walk from marker** is on by default, so the player moves to the start; the marker needs a clear standing space and a floor. Turn it off only to begin at the current player body position. Markers saved before the camera-placement fix keep their stored positions; select each misplaced marker and use **Under camera**, or place it again.

Layout editing is frozen while walking. Checkpoints advance in their authored order, followed by the exit. This progress is preview-only. Press **Esc** to restore the scene and return to editing at the editor camera's previous position and direction. **Reset preview** also ends the preview. If the original player position is clear, a relocated player returns there. The editor camera bookmark applies only to the current raid.

**Unload map** returns to editor home through native scene cleanup without extraction or raid results. The selected draft remains available. Open another map or use **Return to game**.

Editor maps disable normal input actions, damage, survival drain, stamina consumption, AI spawning and raid completion. Normal gameplay HUD and results screens are suppressed. Walking, free-camera editing and editor-controlled door state previews remain available. These restrictions apply only while the editor session is active.

The editor keeps SPT's existing notification connection active during authoring raids so drafts can stay synchronized. If that connection drops, the workspace remains open and shows the connection status while retrying; local draft edits remain recoverable. A walkthrough stops after an extended connection interruption and restores the original scenery.

## Time, weather and map visibility

Open **Session → Environment / time and weather** in the map editor. Enter a time from **00:00** to **23:59** and choose **Set time**, use Dawn/Noon/Dusk/Night, or step by one hour. A selected time stays held for lighting previews; **Use raid time** restores the current raid clock.

Weather has Clear, Cloudy, Rain and Storm presets. Enter percentages for clouds, rain, fog, wind and thunder, then choose **Apply weather**. The wind-direction button cycles through eight compass directions. **Use raid weather** returns to the current native weather. Maps without a sky clock or weather controller show those controls as unavailable.

These are local previews, not saved layout settings. Closing the editor, entering walkthrough, or unloading the map restores time and weather. The raid timer continues. Visibility sampling follows the free camera, including EFT's cached map-culling observer. Unity occlusion culling stays disabled on the editor camera while editing, including after native visibility switches update. Terrain hidden by player-triggered zones is drawn during editing, with its terrain/LOD visibility restored on close or walkthrough. Dedicated editor flight also bypasses baked visibility-cell rejection and baked renderer hiding, so flying outside normal player areas does not inherit an empty visibility set. Player-triggered scene switches are also suspended during dedicated editor flight: their registered components, LOD groups and objects are revealed, including components that ignore inverse zones. Pending hide operations are stopped before editing begins. Native distance and LOD selection remain active; authored hidden objects stay hidden. Closing or entering walkthrough restores captured trigger states, requests native trigger reevaluation and refreshes baked visibility queues. Each scene discovery logs the number of baked groups, trigger targets and disabled targets revealed for troubleshooting. The player is not moved by these corrections.

Live check: fly from the spawn area through distant buildings and above the map; verify scenery follows the camera. Try time and weather presets, reset each independently, enter a walkthrough, and load another map. Check that normal weather, lighting and visibility return afterward.

## Editor windows

The native editor home groups draft selection and map settings in a compact framed workspace. In a loaded map, Browser and Properties are movable, resizable Tarkov-style windows; Environment and Help open separately. Actions sit beside their relevant records, and secondary technical information is under Details. Positions and sizes are remembered between launches. See [Arrange the workspace](raid-authoring.md#arrange-the-workspace) for controls and layout reset.

## Drafts and isolation

The server creates the editor character from a clean native template, with inventory roots and the native empty pockets container for traversal, and no carried gameplay items. It is not linked as an account or campaign character and is excluded from launcher profile lists. Native scratch saves are discarded; a separate editor storage path also prevents scratch files entering gameplay profile storage. Ending a session removes its scratch profile. Sessions expire after one minute without a heartbeat; normal requests then retire abandoned editor state. After a client crash, wait for this lease to expire before retrying a Normal startup. Expired sessions are replaced on editor retry, and abandoned scratch files are cleaned on server startup. Drafts are retained separately.

Map layouts use campaign format **4**; loot placements and native loot/container overrides use format **5**. Formats 1–4 remain supported. Matching client and server components are required for map editing. The authoring service preserves layouts when older authoring clients submit other spatial edits, and rejects older map-editor submissions that could discard format 5 records. Existing draft conflict handling, local recovery, campaign duplication and pack import/export include layouts. Incoming changes wait until walkthrough finishes.

Source map bundles are never rewritten. Targets use map, scene, hierarchy and structural fingerprint; native door IDs are included. Missing, ambiguous or changed targets require explicit rebinding. This milestone does not bake navigation meshes or validate AI routes.

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
