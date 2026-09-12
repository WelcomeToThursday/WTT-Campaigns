# Connected raid authoring

The Campaign Creator and the in-raid editor share a recoverable draft. Publishing a pack is still a separate action. A preview never registers quest triggers, runs story actions, or spawns gameplay objects.

## Connect a raid

1. In the game's BepInEx configuration, enable **WTT-Campaigns → Raid authoring → Enable authoring**. Enter a raid on the map you want to edit. Any character can capture a draft, including a normal character.
2. Open a draft in the administrator's Campaign Creator. In **Connected raid**, choose the advertised map/client and select **Connect draft**.
3. Press **Ctrl+F8**, or use **Create in raid**, **Pick in raid**, or **Edit in raid** beside a compatible web field. A web request opens its focused task when no other screen or unfinished capture owns input.

The raid keeps running. Your character remains in place and can take damage. The editor does not pause AI, the raid timer, or audio.

## Arrange the workspace

The editor opens as one window containing **Library** and **Properties**. Drag the main title bar to move it. Each panel has **Pop out** to detach it and a draggable header. Use **Dock** in the popout or **Return to workspace** in its original slot to return it. Selection and draft fields stay with the panel. **Reset layout** returns all panels to their starting positions. Layout is retained while the editor view exists; it is not saved across game launches.

**Help** toggles the controls reference. **X** closes the editor using the same draft recovery and camera restoration as the keyboard shortcut. Conflict dialogs block the workspace and popouts until resolved or the editor is closed.

The SDK prefab uses EFT's recovered Bender font, native window border and footer gradient, with the campaign interface sounds.

### Expand the UI

`UI/Screens/RaidEditorLayout.cs` registers panels and builds their content. Each module has a stable ID, caption, dock width and position, and a content builder. Keep control names unique. `RaidEditorWindows` discovers panel headers and return slots and owns docking, reset and screen bounds; authoring actions remain in `RaidEditor.Actions.cs`. Window movement is handled by `EditorWindowDrag`.

Run `tools/sync_ui_preview.py`, then **SDK → WTT-Campaigns → Build raid editor** in CJ-SDK. This builds the native uGUI prefab, renders docked/popout/conflict previews at four resolutions, reloads the bundle, and verifies docking, retained draft inputs, reset, bounds and modal ordering. The resulting validation hash is required by `dotnet msbuild build.proj -p:DeploymentScope=Client`, which validates and installs the matching client, UI assembly and bundle with backups. These are offline editor checks; game/server startup is controlled by the user.

## Place and bind content

- **Zones**: create a box or sphere, place it at the player or camera aim point, and edit its position, rotation, dimensions, or radius. Drag the red/green/blue handles to move, rotate, or resize; numeric fields commit when editing ends.
- **Events**: create trigger/interaction bindings, change their kind, or select an existing binding. Select a zone and use **Bind selected zone**, or pick an existing scene object and use **Use scene target**. Conditions, actions, and cinematic media are configured in the web editor.
- **Scene**: search the loaded hierarchy or pick with the mouse. **Select parent** changes the exact binding target. The inspector reports path ambiguity and incompatible collider types.
- **Captures**: save named camera transforms and scene-object references. Captures are authoring records, not spawned objects.
- Finish a web request with **Complete capture**. Cancelling the request does not delete draft records already saved during the task.

Hold the right mouse button to fly: WASD moves, Q/E changes elevation, and Shift boosts speed. Snapping uses 5 cm and 5 degrees; hold left Alt during a drag to bypass it. Ctrl+Z/Ctrl+Y undo and redo. Escape cancels a drag or pick before closing the editor. The shortcut is configurable.

Only the nearest 100 zone outlines are drawn, with the selected zone always included. All records remain searchable.

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
