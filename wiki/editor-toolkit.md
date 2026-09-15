# Editor UI Toolkit

The Campaign Editor uses Unity UI Toolkit throughout: home, campaign-test controls, browsers, inspectors, tool windows, search, scope dropdowns, menus, conflicts and route/AI overlays. The old uGUI Editor controls and fallback have been removed. EFT's screen manager still handles native navigation, environment and input ownership.

All workspaces share one style sheet and reusable controls. Hierarchical browsers use virtualized Toolkit lists, while the Scene catalog retains its ten-record pages and thumbnail cache. Window positions and sizes retain the existing saved layout format. Route authoring remains a separate tool.

## Validation after installation

Manually restart the game client after installing. If server assemblies also changed, manually restart the server before reconnecting.

1. Open Editor home, choose a campaign and map, then enter the map. Check returning home and the disposable campaign test's reset/return controls.
2. Check Layouts, Routes, Zones, Events, Captures, Scene and AI: search, tree expansion, paging, selection, creation and deletion, property edits and undo/redo. Verify scope changes refresh their dependent lists.
3. Inspect original scenery before editing; inspection must not create a saved change. Verify previews, placement, restore and rebind, including native-object restrictions.
4. Type in fields while pressing movement keys. The camera must stay still. Escape releases focus before cancelling a tool or closing. Changing selection must not apply unfinished text to another record.
5. Move, resize, overlap, hide and reopen windows; check tooltips, dropdowns and conflict dialogs. Clicking or scrolling controls must not manipulate the world behind them.
6. Check route and AI markers while moving the camera, walkthrough mode, and repeated map unload/reload. Check 1080p and higher display scales.

Offline validation checks installed Unity API compatibility, absence of uGUI Editor references, control inventories, native lifecycle contracts and asset hashes. Live rendering, input and performance still require in-game acceptance. No performance gain is claimed without measuring frame timing and memory on the same workload.
