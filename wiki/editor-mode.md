# Campaign Editor and mission map layouts

Campaign Editor provides a restricted workspace for authoring the spatial part of a mission. Detailed story, quest and item forms remain in the web Creator. Playable mission raids, encounters, rewards and retries are a later milestone.

## Enter the workspace

From the normal main menu, choose **Campaign Editor**. Pending character operations finish before the client reconnects to a disposable editor character. Entry is unavailable during a raid.

For future launches, set **Campaign editor → Startup mode** to **Editor** in the client configuration, or use the startup button on editor home. Continue launching through the usual SPT launcher. The default is **Normal**. **Return to game** restores the character selected before entry and does not change this preference.

Editor startup opens directly into the full-screen **Campaign Editor** after authentication, using the native EFT menu environment and controls. It also returns here after unloading a map. Select a draft, layout or location from its scrollable choice list. Existing layouts keep their assigned location; choose **New layout in a map** to select another location, then **Open map**. **Refresh drafts** reloads draft choices. If there are no drafts, **Open Creator** to create one first. A failed connection leaves **Retry connection** and **Return to game** available. **Esc** closes a choice list first, or returns to your game character from home. The startup preference remains unchanged when you return.

## Build a route

The existing workspace now includes **Maps**, **Zones**, **Events**, **Captures** and **Scene**. Library and properties panels can pop out and dock again. Changing modules preserves the panel arrangement and selected map layout.

1. In **Maps**, create a layout. Name it in the properties panel.
2. Place one player start, add numbered checkpoints in route order, and place an exit. Captures use the player's location. Use numeric transforms or the Move / Rotate tools to position them after capture.
3. Add barriers to close unwanted passages. Resize barriers and volumes with their size fields or the Scale tool. Checkpoints and exit volumes support box and sphere shapes. **Earlier** and **Later** reorder checkpoints.
4. Use the existing scene picker to select a supported prop. Capture **Move**, **Copy** or **Hide**. Copies can be resized. A captured native door cycles through unchanged, open, closed and locked.
5. Use snapping, numeric transforms and undo/redo to refine the layout. Remove an override to restore its original behavior. **Rebind** explicitly replaces a missing or changed scenery target with the currently picked object.
6. Use the existing draft save controls. Incomplete routes can be saved; walkthrough requires a start, at least one checkpoint and an exit.

Ghosts show proposed scenery changes without moving the original scene. Unsupported objects report why they cannot be edited. Players, bots, loot and extraction systems, quest machinery, scene roots, special doors, animated objects, LOD groups and combined static geometry are excluded. Scenery copies contain supported rendering and collision components only.

## Walk through and leave

**Walkthrough** validates the entire layout and resolves every captured target before applying physical changes. Enable **Walk from marker** to move to the start; the marker needs a clear standing space and a floor. Otherwise walkthrough begins at the current player position.

Layout editing is frozen while walking. Checkpoints advance in their authored order, followed by the exit. This progress is preview-only. Press **Esc** to restore the scene and return to editing. **Reset preview** also ends the preview. If the original player position is clear, a relocated player returns there.

**Unload map** returns to editor home through native scene cleanup without extraction or raid results. The selected draft remains available. Open another map or use **Return to game**.

Editor maps disable normal input actions, damage, survival drain, stamina consumption, AI spawning and raid completion. Normal gameplay HUD and results screens are suppressed. Walking, free-camera editing and editor-controlled door state previews remain available. These restrictions apply only while the editor session is active.

The editor keeps SPT's existing notification connection active during authoring raids so drafts can stay synchronized. If that connection drops, the workspace remains open and shows the connection status while retrying; local draft edits remain recoverable. A walkthrough stops after an extended connection interruption and restores the original scenery.

## Drafts and isolation

The server creates the editor character from a clean native template, with inventory roots and the native empty pockets container for traversal, and no carried gameplay items. It is not linked as an account or campaign character and is excluded from launcher profile lists. Native scratch saves are discarded; a separate editor storage path also prevents scratch files entering gameplay profile storage. Ending a session removes its scratch profile. Sessions expire after one minute without a heartbeat; normal requests then retire abandoned editor state. After a client crash, wait for this lease to expire before retrying a Normal startup. Expired sessions are replaced on editor retry, and abandoned scratch files are cleaned on server startup. Drafts are retained separately.

Map layouts use campaign format **4**. Formats 1–3 remain supported. Matching client and server components are required for map editing. The authoring service preserves map layouts when an older authoring client submits other spatial edits. Existing draft conflict handling, local recovery, campaign duplication and pack import/export include layouts. Incoming changes wait until walkthrough finishes.

Source map bundles are never rewritten. Targets use map, scene, hierarchy and structural fingerprint; native door IDs are included. Missing, ambiguous or changed targets require explicit rebinding. This milestone does not bake navigation meshes or validate AI routes.

## User-controlled acceptance

After installing matching components, manually restart the server and client. Development validation never launches them.

- Enter from a normal character, then repeat with the Editor startup preference.
- Select a draft and map. Move or hide supported scenery, duplicate a prop, adjust a door and barrier, and build a route through a building.
- Save, unload and reopen the layout. Check its transforms, route order and target resolution.
- Start walkthrough with and without relocation. Confirm ordered checkpoints, collision, disabled gameplay and restoration on exit.
- Pop out both panels, switch modules, run a walkthrough and open a second map. Verify controls and selection remain usable.
- Return to game and verify the original character, inventory, progression and normal gameplay. Review the normal profile files for unexpected changes.
- Test a connection interruption and failed map load; retry or return from recovery. Confirm saved drafts survive.

Offline contracts, native assembly checks and Unity previews cover implementation behavior and compatibility. Actual map geometry, native transitions and other installed mods still need this in-game acceptance pass.
