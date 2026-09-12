# Local UI build output

Place the generated `wtt_campaigns_ui.bundle` here, or build it from the companion CJ-SDK project using the project's UI builders. The bundle and Unity manifests are ignored by Git because they are generated and contain recovered game assets.

Client code can compile without this bundle, but running the seasonal UI and creating a complete release package require it. Perk icons are supplied separately from the local `WTT-Campaigns.Assets` directory and copied into the server build.

## Story media

`wtt_campaigns_story_notifications.bundle` holds the custom chapter notification GameObjects, backgrounds, animations and sounds. Shared status icons live in `wtt_campaigns_ui.bundle`; build and install both together. `story-notification-validation.json` is local build evidence used to verify the matching bundle hashes. Use `tools/recover_story_notifications.py` and `tools/unity/CampaignsStoryNotificationBuilder.cs` to rebuild the paired bundles in the matching CJ-SDK.

`StoryMedia/traders.json` and the eight `traders/*.bundle` files are finalized local build dependencies. The custom Peacekeeper room uses beta assets and authored body animation; it has no recovered Peacekeeper voice/facial data. `StoryMedia/examples/story-test.bundle` is a synthetic five-second cinematic with a checksum manifest. These assets are ignored by Git and copied by the full package. See [story authoring](../../docs/story-authoring.md) for custom media registration.

Bundles use `assets/mods/wtt-campaigns.assets/` asset paths. Trader room scripts reference `WTT.Campaigns.UI.Media.StoryRoomLighting` in `WTT-Campaigns.UI.dll`. Rebuild or remap older local assets together with the assemblies and refresh their validated checksum manifests; renaming DLLs alone is insufficient. Unity asset GUIDs stay stable to preserve prefab references.
