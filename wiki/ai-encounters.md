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

Patrol paths use the player checkpoint renderer: outlined connections, numbered markers, labels, selection outlines, screen clipping, and visibility through scenery. Route and roster labels distinguish AI paths from the player's route.

Spawn points and waypoints must be on the existing navigation mesh with standing clearance. Numeric edits and dragging obey the same requirement as placement. Every patrol segment must have a complete path, including the final return segment on a loop. Geometry changes can block a previously valid route, so navigation is checked again before preview and spawn activation. Imported invalid records remain editable, but cannot run. The editor does not rebuild the map's navigation mesh.

BigBrain provides the patrol layer. SAIN keeps control during combat, search, and recovery. If any squad member becomes engaged, the entire squad suspends its authored patrol. When all survivors are eligible, they rejoin at a reachable waypoint; the first surviving member in roster order replaces a dead leader. An unreachable rejoin is reported as suspended, without teleporting bots.

## Observe, playtest, and reset

Compatible **BigBrain 1.5.0** and **SAIN 4.5.1** are required for the initial **SPT 4.1.5** target. Preview remains unavailable when the installed integration cannot establish safe spawn admission and AI ownership. Ordinary raids keep their normal spawning and AI behavior.

- **Observe** keeps the free camera and excludes the editor player from combat targeting. Bots can fight and damage each other.
- Observe sends the mission-start signal. An encounter using **Event** waits for its named event; select that encounter, wave, or roster and use **Simulate**. Selecting a patrol route alone does not select an encounter to activate. Player-entry encounters can also be activated with Simulate. To spawn automatically when Observe begins, use the **Mission start** trigger.
- A patrol route controls movement after spawning. Assign it to the roster to use it. Each encounter runs once per preview; use **Reset preview**, then Observe again for another run.
- **Playtest** places the editor character at the authored player start with fresh health and a disposable copy of the selected game character's equipped gear, including shortcuts for copied equipped items. Combat controls remain native; inventory/container transfers are unavailable during rehearsal. The source character is never used as the active preview identity.
- **Reset preview** cancels pending spawns, removes preview entities and temporary equipment, clears encounter events, and restores the editor. Escape returns directly to editing without opening the native pause menu. Player defeat, spawn failure, connection failure, and map teardown use the same cleanup path. If cleanup reports a failure, resolve it and retry reset before another preview.

Layout editing is frozen while preparing or running a preview. Each new preview has fresh encounter state and gear. Quests, rewards, insurance, persistent loot, and campaign progression do not advance.

## Manual acceptance

Offline checks and installed-file hashes do not establish live compatibility. Restart applications manually after installing matching components, then verify:

- Expand and collapse encounters, waves, and patrols; select their children and confirm the correct properties open. Search for a child, clear the search, and switch browser tabs to check that the tree remains usable.
- No ambient, boss, forced, or external bots appear during editor previews with the installed mod combination.
- Invalid placement is rejected, including disconnected floors and paths blocked by geometry changes.
- SAIN retains uninterrupted combat/search/recovery control and patrols resume correctly afterward.
- Defeat, Escape, reset, and repeated previews restore the editor and leave the source profile unchanged.
- Ordinary raids retain their existing behavior.
- Repeated previews do not accumulate bots or equipment, grow memory continuously, or introduce periodic frame stalls.
