# Editor UI Toolkit

The Campaign Editor uses Unity UI Toolkit throughout: home, campaign-test controls, browsers, inspectors, tool windows, search, scope dropdowns, menus, conflicts and route/AI overlays. The old uGUI Editor controls and fallback have been removed. EFT's screen manager still handles native navigation, environment and input ownership.

All workspaces share one style sheet and reusable controls. Hierarchical browsers use virtualized Toolkit lists, while the Scene catalog retains its ten-record pages and thumbnail cache. Window positions and sizes retain the existing saved layout format. Route authoring remains a separate tool.

## Console

Open **Windows → Console** in the in-game editor. The console can float, resize, or dock beside other tools; its placement and visibility are saved with your workspace. It starts hidden in new and older saved layouts.

Editor action feedback goes directly to the console, including selection guidance, validation warnings and operation errors. The former Notice pop-up and its show/hide/dismiss controls have been removed. These messages are retained while the console is hidden and appear under the default Campaigns filter.

The default filter shows Campaigns messages and command output. Enable **All client logs** to include game and other mod messages. Search and severity filters apply to logs; command results always remain visible. Messages wrap in the scrollable output area. Use the mouse wheel or scrollbar to read older output; scrolling back pauses **Auto-scroll**. Enable it again to follow new messages. Select a message to copy it, or double-click it to expand its details. On narrow windows, scroll the toolbar horizontally to reach its controls.

Rows show aligned timestamps to the second; message details and copied text retain milliseconds. Info is blue, Debug is muted purple, Warnings are amber, and Errors are coral red. Each row also has a matching severity stripe and a written level label.

Use **A− / A+** to adjust console text from 10 to 24, or **Reset** to restore 16. This setting is saved separately from the overall editor UI size and applies to output, expanded details and text fields. Changing it keeps your messages and command history. The window title and toolbar retain their normal size; use the separate overall editor UI-size setting to resize those controls. The window title and toolbar retain their normal size; use the separate overall editor UI-size setting to resize those controls.

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

## Validation after installation

Manually restart the game client after installing. If server assemblies also changed, manually restart the server before reconnecting.

1. Open Editor home, choose a campaign and map, then enter the map. Check returning home and the disposable campaign test's reset/return controls.
2. Check Layouts, Routes, Zones, Events, Captures, Scene and AI: search, tree expansion, paging, selection, creation and deletion, property edits and undo/redo. Verify scope changes refresh their dependent lists.
3. Inspect original scenery before editing; inspection must not create a saved change. Verify previews, placement, restore and rebind, including native-object restrictions.
4. Type in fields while pressing movement keys. The camera must stay still. Escape releases focus before cancelling a tool or closing. Changing selection must not apply unfinished text to another record.
5. Move, resize, overlap, hide and reopen windows; check tooltips, dropdowns and conflict dialogs. Clicking or scrolling controls must not manipulate the world behind them.
6. Check route and AI markers while moving the camera, walkthrough mode, and repeated map unload/reload. Check 1080p and higher display scales.
7. Open Console, dock it, resize it narrowly and restore its layout. Test filters, expanded details, copy, auto-scroll and heavy log output. Check quoted window names, history, Tab completion and the two-stage Escape behavior. Typing commands must not move the camera or activate scene shortcuts. Try undo/redo with empty history and focus with no selection; both should explain why the action is unavailable.

Offline validation checks installed Unity API compatibility, absence of uGUI Editor references, control inventories, native lifecycle contracts and asset hashes. Live rendering, input and performance still require in-game acceptance. No performance gain is claimed without measuring frame timing and memory on the same workload.
