# Map layers in ordinary raids

Map layouts can also provide reusable level content for ordinary raids. They do not need a mission, player start, checkpoints, or an authored exit.

## Authoring

Open **Editor** (automatically shown as **Level Editor** for ordinary layouts), choose a draft and map, and create a named layout in **Layouts**. Use the existing scenery, door, barrier, loot, zone, hazard, and extract tools. **Apply in normal raids** sets its default enabled state. The same switch appears in the Creator's map layouts page.

Publish the campaign to make its layouts available outside the editor. Unsaved edits and unpublished drafts do not change ordinary raids.

## Regular characters

Open **MAP LAYERS** from the main menu. Layers from all published campaigns are grouped by map; each row shows its source campaign and an **ENABLED / DISABLED** switch. Selections are saved for the regular character and take effect on the next raid. You can override a layer's authored default without changing the published campaign.

Enabled layers on the matching map apply together. Conflicting edits to the same object or to both a parent and child are rejected. Disable the conflicting layer and try again. Disabling remains available even when existing selections conflict.

**SHOW TEST ENTRIES** opens eight UI samples grouped under Customs, Interchange, and Woods. Their switches update the sample counts locally, without saving selections or changing raids. Use **SHOW REAL LAYERS** to return to your published content.

## Campaign characters

Ordinary raids use the enabled defaults from that character's own campaign. Regular-character selections do not change campaign behavior. Mission raids use their mission layout separately.

## Included content

Layers apply supported scenery movement, hiding and copies, asset props, doors, barriers, placed loot, loot containers, and terrain texture/grass recipes. Terrain recipes compose in campaign/layout order; later strokes operate on the accumulated result. Disabling a layer removes its contribution on the next raid. Generated container contents are fixed for the raid so repeated requests cannot reroll them.

Enabled levels also apply their layout-owned zones, hazards and native quest/salvage interactions. Quest eligibility uses the active PMC profile. In a campaign, existing story bindings can use enabled level zones; regular PMCs do not gain campaign story progression. Publishing quest/story references to a level zone requires that level to be enabled by default.

Each level may add one extract through **Extracts**. Multiple enabled levels can add multiple extracts. These supplement native extracts and use the native countdown and normal raid outcome handling. Player spawns and normal AI remain native. Authored AI, checkpoints, retries, custom starts and mission completion remain exclusive to Mission Editor. Mission-owned layouts are excluded from ordinary-raid layers even if an old preference enables them. The runtime restores its scenery edits and removes its owned objects during cleanup between raids. Layouts do not modify the original game map assets.

After installing an update, restart the server and client manually. Verify loading, interaction and loot, then run a second raid to check cleanup; offline checks cannot establish those in-game results.
