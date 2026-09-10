# Local UI build output

Place the generated `wtt_campaigns_ui.bundle` here, or build it from the companion CJ-SDK project as described in the root README. The bundle and Unity manifests are ignored by Git because they are generated and contain recovered game assets.

Client code can compile without this bundle, but running the seasonal UI and creating a complete release package require it. Perk icons are supplied separately from the local `WTT-Campaigns.Assets` directory and copied into the server build.

## Story media

`wtt_campaigns_story_notifications.bundle` holds the custom chapter notification GameObjects, backgrounds, animations and sounds. Shared status icons live in `wtt_campaigns_ui.bundle`; build and install both together. `story-notification-validation.json` is local build evidence used to verify the matching bundle hashes. See [chapter notifications](../../docs/story-notifications.md) for recovery, prefab building and offline rendering.

`StoryMedia/traders.json` and the eight `traders/*.bundle` files are finalized local build dependencies. The custom Peacekeeper room uses beta assets and authored body animation; it has no recovered Peacekeeper voice/facial data. `StoryMedia/examples/story-test.bundle` is a synthetic five-second cinematic with a checksum manifest. These assets are ignored by Git and copied by the full package. See [story authoring](../../docs/story-authoring.md) for recovery, finalization and media registration.

Bundles use `assets/mods/wtt-campaigns.assets/` asset paths. Trader room scripts reference `WTT.Campaigns.UI.Media.StoryRoomLighting` in `WTT-Campaigns.UI.dll`. Rebuild or remap older local assets together with the assemblies and refresh their validated checksum manifests; renaming DLLs alone is insufficient. Unity asset GUIDs stay stable to preserve prefab references.
