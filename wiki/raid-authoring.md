# Connected raid authoring

The Campaign Creator and the in-raid editor share a recoverable draft. Publishing a pack is still a separate action. A preview never registers quest triggers, runs story actions, or spawns gameplay objects.

## Connect a raid

1. In the game's BepInEx configuration, enable **WTT-Campaigns → Raid authoring → Enable authoring**. Enter a raid on the map you want to edit. Any character can capture a draft, including a normal character.
2. Open a draft in the administrator's Campaign Creator. In **Connected raid**, choose the advertised map/client and select **Connect draft**.
3. Press **Ctrl+F8**, or use **Create in raid**, **Pick in raid**, or **Edit in raid** beside a compatible web field. A web request opens its focused task when no other screen or unfinished capture owns input.

The raid keeps running. Your character remains in place and can take damage. The editor does not pause AI, the raid timer, or audio.

## Arrange the workspace

The in-game editor uses compact Tarkov-style tool windows: dark panels, thin borders, the recovered Bender font, the existing tool/category icons with visible action labels, and restrained selection highlights. Item and scene thumbnails are preserved. The map stays interactive in the space around the windows.

**Browser** contains labeled **Maps**, **Routes**, **Zones**, **Events**, **Captures**, and **Scene** tabs, search, records, and the creation/picking actions for that category. Scene adds Catalog / In scene / Changes tabs and item filters. Selecting a record opens **Properties** with its relevant fields and actions. Long forms scroll; **Details +** expands technical identity and diagnostic information. Hover over scene records or status text for the complete message.

Drag a window's title bar to move it. Drag its lower-right corner to resize it. The **×** button hides a window; **Windows** reopens Browser, Properties, Environment, or Help. Both Browser and Properties can remain open on smaller displays. Windows stay within the display with readable minimum sizes and scrolling content.

Window positions, sizes, and visibility are saved locally between game launches. They also survive category changes, map transitions, and walkthroughs. **Windows → Reset layout** restores the default floating arrangement. Resizing or reopening a window preserves selection and field values. Preferences are stored under **Campaign editor → Tool window layout** in the client configuration, independently of campaign drafts and profiles.

The compact top toolbar holds undo/redo, move/rotate/resize, snapping, camera speed (**Fly m/s**, 0.25–96 m/s), and walkthrough controls. Shift boosts movement 4×; Ctrl provides precision movement. **Environment** opens time and weather controls in its own scrollable window. **Help** opens the controls reference.

**Session → Unload map / return home** is available in dedicated editor mode. **Close** uses the existing draft recovery and camera restoration. Escape dismisses a menu or releases field editing first, then cancels an active drag/pick, then closes the editor. Dragging and resizing windows do not operate scene tools or fly the camera.

If editor initialization fails, automatic opening pauses after reporting the original error. Press your configured editor shortcut (Ctrl+F8 by default) to retry after resolving the problem, or reopen the map. Failed initialization releases its partial UI and newly loaded bundle.

A web capture request adds a temporary **Complete capture / Cancel** row. Creation and picking remain available inside Browser. The status line distinguishes ordinary raids from dedicated editor sessions. Conflicts display a blocking dialog above every tool window.

### Presentation and asset provenance

The presentation follows the supplied Tarkov-style loot-editor reference. Its appearance uses the existing recovered EFT **Bender** font, **confirmation-border.png** window frame and **footer-gradient.png** header from the CJ-SDK campaign assets (`Fonts` and `SelectionArtwork`). Button hover/click audio uses the campaign interface-sound adapter. No replacement artwork or placeholder icons are introduced. The editor bundle remains self-contained and serializes only native Unity UI components; window movement, resizing, tooltips, and action bindings are attached at runtime.

### Maintain the UI

The shared layout builder defines the toolbar regions and Library/Inspector modules. Keep bound control names unique. The window host manages floating windows, saved layout restoration, responsive bounds, menus, contextual groups, selection visibility, and walkthrough restoration. Client authoring code supplies the current record context without changing the campaign schema or synchronization protocol.

Run `tools/sync_ui_preview.py`, then **SDK → WTT-Campaigns → Build raid editor** in CJ-SDK. The builder renders all categories, box/sphere properties, map doors, popouts, capture requests, conflicts, home, and walkthrough at 1280×720, 1280×1024, 1920×1080, 2560×1440, and 3440×1440. It checks unique controls, contextual fields, capture actions, floating windows, saved layouts, retained fields, reset, bounds, label fit, simultaneous panels, walkthrough restoration, and modal ordering. The resulting bundle hash is required for installation.

Use `dotnet msbuild build.proj -p:DeploymentScope=Client` to validate and install the matching client, UI assembly, and bundle with backups. The shared-assembly compatibility guard must pass; if the installation has an older shared assembly, validate and install the required matching component set together. These checks do not launch the game or server, and offline previews do not establish in-game acceptance.

## Place and bind content

- **Routes** (dedicated editor mode): select a layout and author its player start, ordered checkpoints and exit. New checkpoints insert after the selected checkpoint. Frame waypoints, reorder them, edit their volumes, and choose where the walkthrough starts. See [Camera speed and player routes](editor-mode.md#camera-speed-and-player-routes).
- **Zones**: create a box or sphere, place it at the player or camera aim point, and edit its position, rotation, dimensions, or radius. Drag the red/green/blue handles to move, rotate, or resize; numeric fields commit when editing ends.
- **Events**: create trigger/interaction bindings, change their kind, or select an existing binding. Select a zone and use **Bind selected zone**, or pick an existing scene object and use **Use scene target**. Conditions, actions, and cinematic media are configured in the web editor.
- **Scene**: search the loaded hierarchy or pick with the mouse. **Select parent** changes the exact binding target. The inspector reports path ambiguity and incompatible collider types.
- **Captures**: save named camera transforms and scene-object references. Captures are authoring records, not spawned objects.
- Finish a web request with **Complete capture**. Cancelling the request does not delete draft records already saved during the task.

Hold the right mouse button to fly: WASD moves, Q/E changes elevation, and Shift boosts speed. Snapping uses 5 cm and 5 degrees; hold left Alt during a drag to bypass it. Ctrl+Z/Ctrl+Y undo and redo. Escape cancels a drag or pick before closing the editor. The shortcut is configurable.

Only the nearest 100 zone outlines are drawn, with the selected zone always included. All records remain searchable.

Scene browsing indexes loaded map objects incrementally (at most 128 objects or about 2 ms of work per frame). It excludes player/UI/editor subtrees and retains at most 100,000 objects and 8 million characters of names/paths. Picking stays available while indexing; binding a scene target waits for a complete, unambiguous index. If a map reaches a limit, the editor reports it instead of continuing to allocate. Cached browsing and target checks do not rebuild the entire hierarchy every refresh. The index is released when leaving the raid.

## Scene editing in dedicated editor mode

Use **Scene → Catalog** to browse supported props, loot, or installed presets. Entries retain their names when artwork is loading or unavailable. **N/A** means the preview failed; select the entry and use **Retry preview** in Properties. Prop previews use an isolated, evenly lit rendering of the supported mesh. Loot and presets use their native inventory icons. Preview lighting can differ from the map's weather and lighting.

Search, paging, and background synchronization preserve the selected object. **Place** starts a single placement: point at a surface and click, or press Escape to cancel. The placed object is selected automatically with Move active. **In scene** selects existing objects; **Changes** lists the current layout's edits, including removed objects and their restore action. Unsupported or combined map geometry remains unavailable.

The selected object's bounds are outlined. Handles remain visible through scenery and stay approximately the same size on screen. **Anchor: Center** is the default for scene objects; rotation and resizing keep the visible object's center fixed. Switch to **Anchor: Pivot** to use the original object origin. **Frame (F)** positions the editing camera to fit the selected object, preserving its viewing direction. Framing is unavailable during typing, placement, dragging, menus, or walkthrough. Independent static props, including originals, can be resized. Native loot and containers keep their original size.

Background synchronization does not disable ordinary scene-editing controls. Conflicts still block edits. Each completed handle drag makes one undo entry; Escape restores its starting transform. Unfinished text, panel positions, and inspector scrolling survive ordinary refreshes.

Copied props, moved scenery, and applied mission barriers cut AI navigation around their solid colliders. The cuts follow position, rotation, and size changes and are removed when an edit is undone or the scene is released. Trigger volumes and placement ghosts do not cut navigation. AI preview and mission startup wait for the navigation update before checking routes.

These cuts use a box for each collider; an irregular or hollow mesh can therefore block more space than its visible surface. Moving or hiding original map scenery cannot restore walkable ground that was absent from the map's baked navigation. Check container detours, narrow passages, and undo/redo in Observe or Playtest before publishing a layout.

Assigned AI spawns must also have a complete route in both directions to an AI core point. Preview and mission preparation reuse native cores where reachable. For a valid isolated area, preparation creates one temporary native core per mutually reachable group of assigned spawns, with its own connection group. These cores remain registered until the owned bots finish cleanup and are removed on reset or mission teardown. They do not create walkable ground, bypass blocked spawn checks, or add baked tactical cover.

### AI workspace and navigation inspection

The AI browser separates **Build encounters**, **Navigation**, and **Test encounters**. Use the tree to select an encounter before adding a wave, a wave before adding a roster, and a patrol before adding waypoints. Properties show the selected record's activation, wave timing, bot roster, assignments, or patrol movement. Spawn assignments use a named add/remove menu, and patrol assignment uses a named dropdown.

Select a spawn, then enable **Inspect navigation**. The overlay shows local navigation samples within 14 metres on either side at the spawn's elevation, nearby authored obstacle boxes, and the path toward a core. Green indicates walkable samples or a complete two-way core connection. Amber marks an isolated valid spawn that needs a local core and the authored obstacle boxes. Red indicates missing samples or a disconnected route; the red connector to an unreachable core is a diagnostic line, not a traversable path. Inspection is read-only and refreshes every two seconds. It does not triangulate the whole map, and samples at one elevation do not describe other floors.

For live acceptance, test one assigned bot inside an enclosed area: inspect its spawn, start Observe, confirm a local core is logged and the bot follows its assigned patrol around containers, then reset and repeat. In Playtest, confirm combat can interrupt the patrol and that the bot resumes afterward. Also test a spawn connected to the original map, an invalid spawn, and map unload/reload. Offline checks verify planning and native APIs; they cannot establish live combat, path following, or Unity cleanup behavior.

Authored patrol movement uses a complete path from the live carved NavMesh, checked against solid scenery, and sends those corners to the native mover. It does not recalculate the route through the original map's baked cover graph. Walk pace is applied immediately before native movement. Combat, search, and recovery can still suspend the patrol. A roster with no patrol assignment holds near its authored spawn: after drifting more than three metres away, it walks back within 1.5 metres. Combat, search, and recovery can interrupt this hold too.

Encounter bots also submit nearby authored solid objects to SAIN's tactical-cover analyzer. SAIN still decides whether an object provides useful protection from the current threat; additional checks require clear standing space and a complete live NavMesh path. Moved or inactive candidates are invalidated during cover refresh. Invisible mission barriers are excluded from authored cover candidates. This integration extends SAIN's live cover search; it does not rebuild the original map's baked tactical-cover graph.

Movement updates preserve the native path's corner progress while the path remains clear. Changed destinations, replaced paths, newly blocked segments, or three seconds without meaningful movement trigger a fresh path request. Facing follows the current segment around bends. Movement logs distinguish the current corner from the final target and include the corner index.

For cover and movement acceptance, compare a Walk patrol with an unassigned bot, trigger combat, and confirm each resumes its route or hold afterward. Include several laps around a container corner and a return to spawn around an obstacle. Fight around a placed container, then move or hide it and repeat: cover selection should follow the current solid scenery. These behaviors require in-game verification after a manual restart.

During previews and mission encounters, `AI movement` log entries report each live bot every five seconds: assigned route and waypoint or hold status, suspension reason, active behavior, SAIN decisions, movement ownership, path result, position, and native destination. Cover diagnostics include candidate and accepted counts, authored additions, invalidations, rejections, and selected position. `AI patrol assigned` records identify which generated bots received each route. Include these entries when reporting wandering, stalled movement, cover problems, or failure to resume a patrol.

For manual acceptance after installing and restarting the game: hold and release buttons across several synchronization cycles; search and page while thumbnails load; retry a failed preview; place small loot and a large container; switch Center/Pivot and move, rotate, resize, cancel, undo, and redo; frame an object with an offset pivot; then unload and reopen the map. Confirm that previews, selection, and handles recover without stale objects. Offline rendering and assembly checks do not replace these in-game checks.

## Saving and recovery

Both editors synchronize completed edits while connected. An unfinished text field or drag stays local until committed. Independent changes merge; conflicts show both versions and require a choice. The conflicting choices preserve independent edits.

Unsent client edits are saved under `BepInEx/config/WTT-Campaigns/raid-authoring/<draft-id>.json`. Reconnecting the same draft reconciles them with the current server revision. Capture requests themselves expire with their raid and are never replayed into a new raid. A server restart revokes existing connections; reconnect from the administrator's editor.

The editor restores its camera, cursor, input, and temporary rendering state when closed, interrupted by another screen, disconnected, or ended by death/extraction.

## Published zones

Spatial packs use campaign format 2. Existing format-1 packs keep their serialization and identities. Install matching WTT-Campaigns client and server components before using a spatial pack.

Zones belong to a map and scene and can support:

| Use | Native behavior |
| --- | --- |
| InZone | Supplies the zone identifier to native quest filters. |
| VisitPlace | Reports a native place visit, with zero additional exploration XP. |
| LeaveItemAtLocation | Supplies a native quest-item placement area. |
| Story Trigger/Cinematic | Runs the published binding through the existing story event rules. |

Published zones load for the active campaign character on the matching map. Native quest zones work even when the campaign has no story definition. Draft previews stay separate from these published objects. Publish, then restart the server and game before entering a new raid to test published gameplay. Campaigns already used by characters retain the existing gameplay-lock rule; duplicate them to change gameplay.

Zone references must be reassigned before deletion. Scene paths must resolve uniquely. Shoot targets require a ballistic collider; interaction targets require a raycastable collider. IDs colliding with native map zones are rejected during connected editing and skipped with a diagnostic at runtime.

NPC/item spawning, cinematic camera paths, and specialized native triggers beyond the types listed above are outside this version.

---

[Documentation home](Home.md) · [Guide navigation](_Sidebar.md)


## Scene catalog in Editor mode

For persistent prop placement, fixed loot models, and moving/removing native loot or searchable containers, use **Campaign Editor → Scene**. **Catalog**, **In scene**, and **Changes** keep spawning, selection, and restoration separate. These edits require a selected map layout and are unavailable in ordinary raid-authoring sessions. See [Dress the scene](editor-mode.md#dress-the-scene) for placement, cancellation, rebinding, and save/reopen behavior.

Click an object in the Scene workspace to select it, highlight its bounds, and open its properties. Trigger volumes do not block clicks; collision-only children resolve to their visible parent, and scenery without colliders can be selected by its bounds. Selection does not create a draft edit. The first changed property or completed drag creates the edit; Escape restores a cancelled drag. Rotate uses colored rings, and Resize uses the object’s colored axes. Objects with unsupported gameplay components or combined static meshes remain selectable for inspection, with the restriction shown above read-only properties.

Live acceptance: click an original prop without capturing it first, rotate/resize it, cancel a second drag, and undo/redo the first edit. Check a collider-free placed item, a prop behind a trigger, an LOD prop, and a restricted object. Each click should expose the matching properties without changing the draft.

In dedicated editor mode, clicking scenery from **Maps** opens the selected object in **Scene** with its highlight and transform properties. **Pick scenery** and choosing a prop or loot record in the Maps library use the same object inspector. Layouts, doors, start/exit markers, checkpoints, barriers, and ordinary raid capture tools retain their existing workflows.

Regression acceptance: start in **Maps** with an existing copied container selected (as in the reported recording), then click a different original container, a crate, and the copy again. Each click must select the corresponding object and show its Scene properties. Repeat with **Pick scenery**, and by choosing a prop row in Maps. Verify clicks on editor panels do not select scenery, handles still drag, and returning to Maps retains the selected layout.

Original props with rigid bodies, audio sources, lights, base/composite ballistic components, thermal effects (`HotObject`), static decals, or stencil shadows can now be transformed while retaining those components. Thermal positions, decal registration, and shadow bounds are refreshed after movement and restoration. Original physics is held during editing and restored on undo, removal, or unload. These extra components do not become copyable: the Catalog contains reproducible props, and the inspector explains when an original can move but cannot be copied.

Combined static batches, destructible windows, damage triggers, baked acoustic geometry, and unadapted components still require dedicated support. Test movement, rotation, numeric transforms, centered resize, Escape, undo/redo, restoration, and map unload on thermal/decal/shadow props and physics props after manually restarting the game. Check the effects at the new position and again after restoration.

Props whose native collision meshes are not readable retain their original size. Resize handles and size fields are unavailable for those props, and the inspector explains the restriction. Movement and rotation remain available when the object otherwise supports them. Saved or remote resize edits are also checked before changing the scene; an unsupported edit reports an error and restores the original prop instead of repeatedly breaking its collision mesh. Remove or undo that resize edit before trying again.

Editor tooltips appear after a short hover, fit short captions, and wrap longer help within the window. Clicking a control or leaving it dismisses the tooltip.
