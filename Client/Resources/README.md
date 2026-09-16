# Local UI build output

Place the generated `wtt_campaigns_ui.bundle` here, or build it from the companion CJ-SDK project using the project's UI builders. The bundle and Unity manifests are ignored by Git because they are generated and contain recovered game assets.

Client code can compile without this bundle, but running the seasonal UI and creating a complete release package require it. Perk icons are supplied separately from the local `WTT-Campaigns.Assets` directory and copied into the server build.

## Editor Toolkit

The entire Editor requires `wtt_campaigns_editor_toolkit.bundle` and its local `editor-toolkit-validation.json` build evidence. Rebuild these with **Unity 2022.3.43f1**, matching EFT: copy `tools/unity/CampaignsEditorToolkitBuilder.cs` into the companion CJ-SDK project's Editor folder, then run its `CampaignsEditorToolkitBuilder.Build` method. The builder copies the repository's UXML/USS, includes the recovered Bender font and embeds copies of all three runtime UI Toolkit shaders plus the scene-preview shader. It checks import, bundle dependencies and asset reload before writing the SHA-256 evidence.

These generated files remain ignored like the other recovered game assets. `dotnet msbuild build.proj` validates and installs the bundle with its matching client assembly. See [the Editor guide](../../wiki/editor-toolkit.md) for scope and live acceptance checks.

## Native container library

`ContainerLibrary/catalog.json` and `ContainerLibrary/native-containers.bundle` are generated locally from the installed game's serialized map objects. They preserve the native container body, lid, colliders, interaction settings, materials, sounds and dependencies. These recovered assets remain ignored by Git.

Run `.tools/Scripts/python.exe tools/export_container_library.py --output Client/Resources/ContainerLibrary` to scan the installed maps and rebuild the library. The importer reads game files without loading maps or launching the game. Use `--game` to override the default installation. The generated catalog records the native assembly fingerprint and bundle hash; `--verify` checks those hashes, native template coverage and every serialized object reference. MSBuild includes that check and deploys the library alongside the client.

For an engine-side asset check, copy `tools/unity/CampaignsContainerLibraryCheck.cs` into a Unity 2022.3.43f1 Editor folder, set `WTT_CONTAINER_BUNDLE` to the generated bundle's absolute path, and execute `CampaignsContainerLibraryCheck.Run`. The SDK check covers models, meshes and material references; native interaction and loot ownership still require an in-game check after the user restarts the client and server.

## Story media

`wtt_campaigns_story_notifications.bundle` holds the custom chapter notification GameObjects, backgrounds, animations and sounds. Shared status icons live in `wtt_campaigns_ui.bundle`; build and install both together. `story-notification-validation.json` is local build evidence used to verify the matching bundle hashes. Use `tools/recover_story_notifications.py` and `tools/unity/CampaignsStoryNotificationBuilder.cs` to rebuild the paired bundles in the matching CJ-SDK.

`StoryMedia/traders.json` and the eight `traders/*.bundle` files are finalized local build dependencies. The custom Peacekeeper room uses beta assets and authored body animation; it has no recovered Peacekeeper voice/facial data. `StoryMedia/examples/story-test.bundle` is a synthetic five-second cinematic with a checksum manifest. These assets are ignored by Git and copied by the full package. See [story authoring](../../wiki/story-authoring.md) for custom media registration.

Bundles use `assets/mods/wtt-campaigns.assets/` asset paths. Trader room scripts reference `WTT.Campaigns.UI.Media.StoryRoomLighting` in `WTT-Campaigns.UI.dll`. Rebuild or remap older local assets together with the assemblies and refresh their validated checksum manifests; renaming DLLs alone is insufficient. Unity asset GUIDs stay stable to preserve prefab references.
