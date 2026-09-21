# AI encounters and patrol previews

AI authoring belongs to a selected map layout in Campaign Editor. It is separate from the player checkpoint route. Preview encounters in a disposable editor session, then use them in [playable campaign missions](missions.md).

## Build an encounter

The AI browser uses an expandable tree. Encounters contain their trigger volumes and ordered waves; each wave contains its roster entries. **Spawn points** and **Patrol routes** are separate branches because multiple rosters can reference them. Expand a patrol to see its waypoints in order.

Use the arrow beside a branch to expand or collapse it, and select a record's name to edit its properties. Search keeps matching records under their parents and reveals the matching branches. Clearing search restores your expanded and collapsed branches. The tree scrolls continuously, without splitting an encounter across pages.

1. Open a saved draft and map, select a layout, and choose **AI**.
2. Add spawn points at clear standing positions on the map's existing navigation mesh. Each bot in a wave needs its own assigned position. Extra bots are never stacked at one marker or redirected to ordinary map spawn points.
3. Add an encounter and choose its activation: player entry into its trigger volume, mission start, or a named event. Preview event controls simulate these signals without changing campaign progress.
4. Add ordered waves. Set a delay, then choose whether it runs after the preceding wave activates or after all of that wave's bots are defeated. An encounter activates only once per preview run, even if the player reenters its trigger or an event repeats.
5. Add roster entries with role, difficulty, count, spawn assignments, and optional squad and patrol assignments. Equipment comes from the installed bot generator. Custom loadouts, factions, bosses, and unverified special/modded roles are outside this milestone.

Incomplete work can be saved. Preview requires complete assignments and valid navigation. A failed generation or activation stops the preview and returns to editing with the failure reason; a missing bot is never counted as a defeated bot.

## Patrol routes

Add a patrol and place its waypoints in order. Configure a wait at each waypoint, walking or running pace, and loop, ping-pong, or stop-at-end behavior. New patrols walk, loop, and have no waits.

Select a patrol or one of its waypoints to see its calculated walkable path, direction arrows, and segment distances. Other routes retain their lightweight overview. Loop closures and both ping-pong directions are checked. Green paths are complete; dashed amber connections are pending and dashed red connections have failed. These diagnostic connections are not walkable paths. **Inspect navigation** opens detailed results in a scrollable legend, including the failed waypoint pair and reason. Distances include the return journey for ping-pong routes. Inspection refreshes as edits change navigation and every two seconds, with up to four directed segments checked per frame.

**Add Waypoint** appends to the route. Select a waypoint and use **Insert after selected** to insert at the floor beneath the camera, **Move earlier / Move later** to change its order, or **Reverse route** to reverse the full sequence. Each operation supports undo and redo. Waits stay attached to their waypoints, and newly inserted points start with zero wait.

Waypoint drafts may temporarily be off the NavMesh, obstructed, or disconnected. Dragging, numeric edits, and camera-floor placement allow these temporary positions so a broken route can be repaired one point at a time. Errors remain visible until corrected. Spawn placement still requires clear standing room on the existing NavMesh. Observe, Playtest, and mission activation require every waypoint and directed route segment to be valid. Ordinary route editing does not rebuild navigation. Use the dedicated [manual navigation tools](editor-toolkit.md#manual-navigation-tools) for bounded editor previews that retain native navigation.

BigBrain provides the patrol layer. SAIN keeps control during combat, search, and recovery. If any squad member becomes engaged, the entire squad suspends its authored patrol, preserving its target and direction and freezing any remaining waypoint wait. Once all survivors are eligible, the remaining wait resumes, followed by the saved target. If that target is unreachable, the squad rejoins at the waypoint nearest its leader that every survivor can reach, preserving its travel direction. Re-entry is logged. If no common waypoint is reachable, the squad remains suspended and retries without teleporting. The first surviving member in roster order replaces a dead leader without resetting patrol progress. Checkpoints retain target, direction, and the frozen wait.

## Spline paths and smoother corners

Select a patrol or waypoint, then choose **Edit Spline** in Properties. This captures the currently walkable path, including its navigation corners, as editable curve points. Existing routes keep their original behavior until you enable this tool.

- Select a point in the viewport. Drag it freely in the camera plane, use the colored axes, or edit its world X/Y/Z coordinates. **Edit** selects the point, incoming handle, or outgoing handle. Snapping uses the editor grid; hold Alt to bypass it.
- **Corner** keeps a sharp turn. **Auto** creates a smooth tangent. **Aligned** keeps the two handle directions linked; **Free** lets each handle move independently. Dragging an Auto handle changes it to Aligned.
- **Insert point** splits the next curve segment without changing its shape. Ctrl-click a curve to split near the cursor. Added shaping points have no gameplay wait or checkpoint behavior. **Delete point** removes a shaping point; use the existing route tools to remove gameplay waypoints.
- Set **Smoothing (%)** (initially 50), then choose **Smooth selected** or **Smooth route**. Authored waypoints remain fixed. Navigation corners round off only where clearance permits; tight corners keep a smaller radius or their original shape. **Make corner** collapses both handles.
- Each drag or action supports undo/redo. Escape cancels a viewport drag. **Finish spline editing** hides the editing handles while retaining the saved curve.

Patrol curves automatically follow the connected ground between waypoints. Smoothing or adjusting handle height no longer lifts the walking path off the floor. Free and X/Z point drags snap to nearby ground on the current floor; explicit Y edits remain available for deliberate floor changes. Waypoint positions and waits stay fixed during smoothing. Ground following preserves the horizontal curve and still rejects gaps, sideways snapping, disconnected floors, and insufficient clearance. Player route guides remain freely editable in 3D.

Green patrol curves have passed walkable-ground and clearance checks. While dragging, the whole curve redraws immediately; navigation checks catch up quietly. Purple marks off-NavMesh ground or a floor mismatch, orange marks insufficient standing clearance, and red marks a blocked connection. A white cross inside a colored diamond locates the first failed check. The warning fades over the nearby three metres of curve into grey; grey portions are unconfirmed, not a guarantee of clearance. The label and legend identify the affected waypoint pair and reason. Amber is used for geometry awaiting its first check; routine background refresh keeps the settled display.

A curve through a wall, across a floor edge, or outside walkable ground blocks preview and mission activation. Bots follow the validated samples, with a navigation connector when joining or resuming. They do not replace a blocked authored curve with an automatic detour. New obstructions suspend the patrol until the curve is clear again. Waypoint waits, patrol pace, loop closure, ping-pong return, and combat ownership still apply.

The **Routes** tool uses the same spline controls for the player start/checkpoint/exit guide. Player curves are visual guides: progression still uses the existing checkpoint and exit volumes, and player movement is not constrained to the curve. Player guide curves do not require bot navigation clearance.

Spline-bearing campaigns use format 12 and require matching updated Campaigns components. Saved data contains authored points and handles; evaluated samples are rebuilt locally.

## Observe, playtest, and reset

Compatible **BigBrain 1.5.0** and **SAIN 4.5.1** are required for the initial **SPT 4.1.5** target. Preview remains unavailable when the installed integration cannot establish safe spawn admission and AI ownership. Ordinary raids keep their normal spawning and AI behavior. BigBrain, Waypoints, SAIN, MoreBotsAPI and Black Division are required for all campaign play; see the [requirements](../README.md#requirements) for minimum versions. The shipped `WTT-Campaigns.AI.dll` is loaded only when an authored encounter needs the integration and both dependencies pass compatibility checks; keep it alongside the matching client assembly when updating.

- **Observe** keeps the free camera and excludes the editor player from combat targeting. Bots can fight and damage each other. With a manual navigation preview, Observe verifies and retains the owned navigation edits and prepared scenery. End observation before editing or clearing navigation. Clear the preview before player playtests, walkthroughs or mission rehearsals.
- Observe sends the mission-start signal. An encounter using **Event** waits for its named event; select that encounter, wave, or roster and use **Simulate**. Selecting a patrol route alone does not select an encounter to activate. Player-entry encounters can also be activated with Simulate. To spawn automatically when Observe begins, use the **Mission start** trigger.
- A patrol route controls movement after spawning. Assign it to the roster to use it. Each encounter runs once per preview; use **Reset preview**, then Observe again for another run.
- **Playtest** places the editor character at the authored player start with fresh health and a disposable copy of the editor character's starting kit, including its equipment shortcuts. Combat controls remain native; inventory/container transfers are unavailable during rehearsal. The source character is never used as the active preview identity.
- **Reset preview** cancels pending spawns, removes preview entities and temporary equipment, clears encounter events, and restores the editor. Escape returns directly to editing without opening the native pause menu. Player defeat, spawn failure, connection failure, and map teardown use the same cleanup path. If cleanup reports a failure, resolve it and retry reset before another preview.

Layout editing is frozen while preparing or running a preview. Each new preview has fresh encounter state and gear. Quests, rewards, insurance, persistent loot, and campaign progression do not advance.

## Encounter failure recovery

The **Campaign AI** configuration section controls the live authored workload. Defaults are **64 maximum active bots**, **one concurrently spawning wave**, and **eight navigation checks per frame**, split between patrol planning and patrol/hold movement. Changes apply to the next preview or mission runtime. These limits do not alter ordinary raid spawning, SAIN combat, or mandatory spawn/navigation preflight validation.

Ready waves wait in arrival order until their whole roster fits the available capacity. Generation reserves that capacity before requesting profiles; living bots continue occupying it until death. Native actor activation starts at most once per frame, including checkpoint restoration. A single wave larger than the configured limit is rejected before spawning with its required count; raise the limit or reduce that wave. The preview status shows queued waves and the active-bot cap.

Patrol route searches spread across frames and rotate between squads. A deferred search is not an unreachable route, and previously owned paths remain intact while checks wait. Results are cached only within one planning pass, then checked again on the next pass. Combat and recovery still override authored movement immediately.

Profile requests retry recognized network/timeout failures up to three total attempts, with short delays. Invalid profiles, rejected identities and native activation failures are not automatically replayed. A native bot profile remains single-use within its attempt.

If a wave only spawns partly, its registered bots and AI bindings are removed. Spawn observations are published only after the whole wave is active. A cleanup error remains visible and requires a checkpoint retry or preview reset.

During a mission, an unrecoverable encounter error pauses the attempt and records a technical interruption separately from player defeat. **Retry checkpoint** restores the last saved checkpoint with a fresh attempt identity when checkpoint retries are enabled. **End attempt** uses the native alive exit path; it does not kill the player or award mission completion. If the server cannot acknowledge the interruption, the attempt stays paused until communication succeeds. Editor previews return to editing through their existing reset path.

## Manual acceptance

Offline checks and installed-file hashes do not establish live compatibility. Restart applications manually after installing matching components, then verify:

- Expand and collapse encounters, waves, and patrols; select their children and confirm the correct properties open. Search for a child, clear the search, and switch browser tabs to check that the tree remains usable.
- No ambient, boss, forced, or external bots appear during editor previews with the installed mod combination.
- Invalid placement is rejected, including disconnected floors and paths blocked by geometry changes.
- SAIN retains uninterrupted combat/search/recovery control and patrols resume correctly afterward.
- Defeat, Escape, reset, and repeated previews restore the editor and leave the source profile unchanged.
- Ordinary raids retain their existing behavior.
- Repeated previews do not accumulate bots or equipment, grow memory continuously, or introduce periodic frame stalls.
- Trigger multiple encounters near the active-bot cap: queued waves should start in order as capacity becomes free, and a checkpoint retry should restore queued waves once. Check long patrols and combat interruptions under a low navigation budget; budget deferral must not appear as an unreachable route.
- Exercise an interrupted profile request and a partial-wave activation failure: retries remain bounded, no duplicate bots appear, and incomplete waves produce no spawn/completion observations.
- For a mission with checkpoint retries, verify an AI interruption offers the saved checkpoint, restores surviving actors once, and permits normal progress afterward. Ending the interrupted attempt must not record a player death or unlock mission rewards. Repeat with checkpoint retries disabled to verify the end-only path.

On the main toolbar, choose **Placeholder kit** (default) or **Copy main-profile kit** before starting a playtest. Both use disposable item copies; the main profile is unchanged. The selection lasts for the current editor session. Reconnect the editor after installing this update to prepare its placeholder kit.
