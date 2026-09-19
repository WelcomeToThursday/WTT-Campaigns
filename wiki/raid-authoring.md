# Connected raid authoring

The Campaign Creator and the in-raid editor share a recoverable draft. Publishing a pack is still a separate action. A preview never registers quest triggers, runs story actions, or spawns gameplay objects.

## Connect a raid

1. In the game's BepInEx configuration, enable **WTT-Campaigns → Raid authoring → Enable authoring**. Enter a raid on the map you want to edit. Any character can capture a draft, including a normal character.
2. Open a draft in the administrator's Campaign Creator. In **Connected raid**, choose the advertised map/client and select **Connect draft**.
3. Press **Ctrl+F8**, or use **Create in raid**, **Pick in raid**, or **Edit in raid** beside a compatible web field. A web request opens its focused task when no other screen or unfinished capture owns input.

The raid keeps running. Your character remains in place and can take damage. The editor does not pause AI, the raid timer, or audio.

## Arrange the workspace

The in-game editor uses compact Tarkov-style tool windows: dark panels, thin borders, the recovered Bender font, the existing tool/category icons with visible action labels, and restrained selection highlights. Item and scene thumbnails are preserved. The map stays interactive in the space around the windows.

The vertical rail directly below **Undo/Redo** opens **Layouts**, **Routes**, **Zones**, **Events**, **Captures**, **Scene**, and **AI**. These tool windows can stay open together. **Hazards** has its own warning-triangle tool for authored hazard areas. Each retains its own search, filters, page, scrolling, tree expansion, and selection. Clicking a rail icon opens or focuses that tool; its edge marker shows that it is open, and the highlighted icon identifies the active tool. Creation and picking actions stay inside their tool window.

Selecting or interacting with a tool makes it active for scene editing and the shared **Properties** window. Hovering does not change the active tool. Scene retains its Catalog / In scene / Changes tabs and item filters. Long forms scroll; **Details +** expands identity and diagnostic information. Hover over records or status text for complete labels and messages.

Drag a window title or dock tab to move it. Drop on a workspace edge or the edge of another dock to create a split; drop in a dock's center to combine windows as tabs. A translucent preview shows the destination. Drag tabs to reorder them or out into the scene to float them. Drag dividers to resize docked groups, or the lower-right corner to resize a floating window. Escape cancels an unfinished drag. Docking reserves space for the scene viewport and rejects splits that would make tools too small.

The game is displayed in the central viewport, which resizes with the dock dividers and uses its own aspect ratio. Begin right-mouse camera flight inside this view; selection, placement, handles and route markers follow the same viewport. Floating tools block clicks through them. Walkthrough and playtest restore the normal fullscreen game view. The viewport presents the existing camera output, so it does not render a second scene or reduce the game render resolution.

The viewport toolbar provides camera speed, snapping, overlay options, clean view, and **Maximize / Restore**. Double-click **Scene** or press **Shift+Space** with the pointer over the view to maximize it; restoring preserves your dock layout. Escape restores a maximized view before closing the editor. Press **G** over the view to toggle clean view. Overlay choices for zones, routes, AI, selection bounds, and handles are saved; clean view temporarily hides them without changing those choices. Routes and AI overlays remain visible across authoring tools when enabled. Changing an overlay option leaves clean view so the choice takes effect immediately.

Hold **Alt+left mouse** to orbit the selection, or **middle mouse** to pan. Without a selection, navigation uses a point in front of the camera. **Right mouse + WASD**, with **Q/E** for elevation, remains free flight. Navigation must begin inside the viewport and stays captured until you release the button. Releasing returns the pointer to its original position; switching applications cancels that restoration.

The **×** button hides a window without clearing its session state. The rail reopens category windows; **Windows** reopens the current tool, Properties, Environment, Help, or Console. Console starts hidden, including when restoring an older layout. Action feedback, warnings and errors are retained there instead of in a Notice pop-up; see the [console controls and commands](editor-toolkit.md#console). New tool windows join the left dock as tabs. Floating tools initially fit their content; manual resizing takes precedence. Narrow windows wrap actions and stack field labels, while lists and long action sections scroll separately. Trees use 24-pixel rows with centered labels, consistent indentation, and disclosure arrows only on branches. Text-only browser rows are 28 pixels tall; scene thumbnails keep their larger rows.

Browser layout follows the Unity Editor's compact toolbar and hierarchy pattern. Action buttons fit their labels, search stays together in a bounded field, and Scene tabs and filters share a wrapping toolbar. Record counts and paging share a footer. Record selection spans the list width, with borderless rows instead of outlined action buttons.

The Scene **Catalog** defaults to a thumbnail grid for Props, Loot, and Presets. **Grid / List** switches presentation while preserving the selected record and keeping the first visible record on the new page; the choice lasts for the editor session. Grid pages fill the available columns and rows, up to 60 previews. Resizing recalculates the page capacity. Hover for the full name or preview error. **Previous / Next** navigates the results; list mode uses ten records per page. **In scene** and **Changes** retain list views.

Window positions, preferred sizes, visibility, dock splits, and tab order are saved locally between game launches. They survive map transitions and walkthroughs. **Windows → Reset layout** restores Layouts on the left and Properties on the right when a record is selected. On smaller displays, dock groups can combine as tabs to keep the scene and tools usable. Search and selection state last for the editor session. Preferences remain under **Campaign editor → Tool window layout**, independently of campaign drafts and profiles. Older browser layouts migrate to the Layouts window.

**Windows → Smaller / Larger** adjusts text, icons, fields, and buttons together in 5% steps (60–130%). The default is 85%; **Reset UI size** restores it independently of your window layout. Size is saved locally under **Campaign editor → UI size percent**. The editor also scales down to fit smaller game windows and uses the extra horizontal space on ultrawide displays.

The compact top toolbar holds undo/redo, move/rotate/resize, snapping, and walkthrough controls. Camera speed (0.25–96 m/s) is set in the viewport toolbar. Shift boosts movement 4×; Ctrl provides precision movement. **Environment** opens time and weather controls in its own scrollable window. **Help** opens the controls reference.

With an object selected, **W / E / R** chooses Move / Rotate / Scale. These shortcuts are inactive while typing, using menus, dragging, placing a preview, or holding RMB to fly. The selected object's Properties actions also include Scale; unsupported transforms remain disabled. Click world objects from any editor tool to select them and open their Scene properties. Zone markers and active capture/picking tools retain their specific selection behavior.

**Session** and **Windows** open anchored dropdown menus. Hover between their toolbar buttons to switch menus; click outside or press Escape to dismiss. Up/Down selects an enabled entry and Enter activates it. Choosing an action closes the menu; UI-size adjustments stay open for repeated changes.

**Session → Unload map / return home** is available in dedicated editor mode. **Close** uses the existing draft recovery and camera restoration. Escape dismisses a menu or releases field editing first, then cancels an active drag/pick, then closes the editor. Dragging and resizing windows do not operate scene tools or fly the camera.

If editor initialization fails, automatic opening pauses after reporting the original error. Press your configured editor shortcut (Ctrl+F8 by default) to retry after resolving the problem, or reopen the map. Failed initialization releases its partial UI and newly loaded bundle.

A web capture request adds a temporary **Complete capture / Cancel** row. Creation and picking remain available inside each tool window. The status line distinguishes ordinary raids from dedicated editor sessions. Conflicts display a blocking dialog above every tool window.

### Presentation and asset provenance

The presentation follows the supplied Tarkov-style loot-editor reference. Its appearance uses the existing recovered EFT **Bender** font, **confirmation-border.png** window frame and **footer-gradient.png** header from the CJ-SDK campaign assets (`Fonts` and `SelectionArtwork`). Button hover/click audio uses the campaign interface-sound adapter. No replacement artwork or placeholder icons are introduced. The editor bundle remains self-contained and serializes only native Unity UI components; window movement, resizing, tooltips, and action bindings are attached at runtime.

### Maintain the UI

The shared layout builder defines the toolbar regions and Library/Inspector modules. Keep bound control names unique. The window host manages floating windows, saved layout restoration, responsive bounds, menus, contextual groups, selection visibility, and walkthrough restoration. Client authoring code supplies the current record context without changing the campaign schema or synchronization protocol.

Author fixed layouts and styling in `tools/unity/EditorToolkit/*.uxml` and `Editor.uss`. Use Unity **2022.3.43f1** and run `dotnet msbuild build.proj -t:Validate -p:RebuildEditorToolkit=true` to rebuild the Toolkit bundle and run offline checks. Set `-p:UnityEditorExe=` to your installed Unity executable; the [build guide](../contributing/editor-presentation.md) includes an example. Control inventories, required templates, native runtime APIs and bundle hashes must pass before installation. C# owns bindings, input, docking and runtime geometry; it does not replace the authored templates. See [Editor UI Toolkit](editor-toolkit.md#validation-after-installation) for manual acceptance across window sizes, docking and display scales.

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

Search, paging, and background synchronization preserve the selected object. Clicking a catalog item starts its placement preview; clicking another replaces that preview. Point at a surface and click to place, or press Escape to cancel. Placement keeps the Catalog tab and page open. Click an item again or use **Place** to start another placement. **In scene** selects existing objects; **Changes** lists the current layout's edits, including removed objects and their restore action. Unsupported or combined map geometry remains unavailable.

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

Also repeat Playtest after killing bots and returning to the editor. Reset removes corpse registrations through native loot cleanup and returns player models through the game's asset pool so later previews can reuse intact models. Test both surviving bots and corpses across consecutive previews.

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


### Game-wide scene catalog

The Scene catalog has an **All game / Current map** source dropdown and **Props / Containers / Loot / Presets** categories. All game combines discovered map objects with independently loadable assets registered by the installed game and mods. Search matches asset names and bundle paths. Current map limits scenery to the loaded map and inventory entries to discovered loot templates.

The first visit incrementally inspects native bundles while the asset catalog is open. Its progress appears below the results. Metadata is cached locally and checked against bundle/dependency file changes on the next session; models use EFT dependency leases and thumbnails use the existing bounded preview cache. Indexing pauses when you leave the asset catalog. Cached results do not imply every object is placeable: map scene/configuration resources, missing components, linked gameplay systems and unavailable native container mappings show an explanation. Select an unavailable entry and use **Retry preview** to retry its discovery/mapping. The catalog never launches or loads another playable map.

Asset placements save their bundle and asset identity in campaign format **8**, and require authoring protocol **5**. Existing map-bound copies and edits retain their original identities and formats. Creator shows saved asset references and container roles; absent resources remain saved and report an unresolved placement when applied. Updated clients and servers must be installed together.

**Containers** preserve native opening, searching and item transfer. Supported current-map containers can be copied on that map; independently loadable container prefabs can be placed elsewhere when they have a supported native loot template. Containers retain their original size. Linked map triggers/glass, special event components and unsupported external references remain unavailable. Editing shows inert previews. Walkthroughs and playable tests obtain random native contents from the server, using the target map's mapping when present or a deterministic source-map mapping for that container type. Repeated requests in a run return the same generated items. Mission contents are persisted with the prepared run, including across server reloads. Each fresh run generates its own contents; catalog browsing never generates takeable loot.

The locally generated **native container library** adds map-embedded containers to **All game → Containers**, independently of the loaded map or background prop scan. The installed game scan currently supplies 45 native container templates, including weapon boxes, sports bags, wooden supply crates, jackets, safes and caches. Current map filters that library to templates discovered in the loaded map. Models retain native bodies, lids, collision and interaction components; no other map is loaded for placement.

Click a placed container in the scene, or select it in **Changes** or **In scene**, to open its dedicated **Loot configuration** tool. Move, resize, dock or close this window, and reopen it with **Loot** on the tool rail or **Windows > Loot configuration**. Selecting another object clears the tool. It provides:

- **Contents:** random native loot, fixed contents, or empty.
- **Loot pool:** use this container's pool or another native container pool. The placed model retains its own capacity.
- **Spawn chance:** 0–100%, rolled once for each new run. A missed spawn is saved with that run too.
- **Fixed contents:** search by item name, select a match, enter a quantity, and choose **Add item**. Weapons and other compound items use SPT's native assembly defaults. Quantities split into legal stacks. Oversized contents report an error instead of silently dropping items.
- **Lock:** use the separate key search under **Access**, choose **Use selected key**, then toggle **Locked**. Unlocking uses the native interaction and key rules.

Configured containers require campaign format **9** and authoring protocol **6**. Older clients cannot overwrite a layout containing these settings. Catalog previews remain inert; fresh walkthroughs and mission runs apply the saved configuration.

The editor retains up to 64 container-run receipts per connected session without eviction, preventing retries from rerolling old results. Reconnect the editor session if that limit is reached. Scene-item eligibility is independent of trader prices/blacklists, but quest objects and inventory infrastructure remain excluded. Missing world models and malformed item assemblies report individual catalog errors.

After installation, manually restart the client and server. Check a cross-map asset's placement, collision, save/reload and removal; then start a fresh walkthrough, search a container, transfer an item, reopen it to confirm no reroll, and return to editing to check cleanup. Offline validation does not establish live Unity visuals or other-mod compatibility.
