# Compact trader media and Visit

The eight finalized trader bundles total **1,686,106,807 bytes (1.69 GB)**, down from **4,121,698,556 bytes (4.12 GB)**: a **59.1% reduction**. These figures cover the trader bundles, not the entire mod or local development backups.

The reference [spt-tradermod](https://github.com/bmpq/spt-tradermod/blob/main/tradermod.eft/Bep/TraderBundleManager.cs) loads a shared vendor bundle before individual scenes. This change keeps Campaigns' independently verified room bundles and reduces their texture payloads instead; it does not import the reference mod's implementation or assets.

## Media policy

`tools/compact_story_traders.py` copies existing BC7 mip tails, caps recognized actor textures at 1024 pixels and other Texture2D assets at 512 pixels, and repacks streamed resources with duplicate payloads stored once per bundle. Name matching for the actor budget is explicit in `ACTOR`; review that list when adding a trader. Unsupported texture formats and textures without mip chains remain unchanged. Cubemaps retain their original resolution.

This is a deliberate texture quality trade-off, especially for readable posters and small prop details. Texture channels, alpha, compression format and remaining mip levels are preserved without another lossy encoding pass. Animation clips, rig/mesh content, speech and scripts are retained. The bundles keep chunked LZ4 compression for direct local loading; they do not require a full-bundle LZMA expansion cache.

The compactor writes candidates separately, checks source manifest hashes, reloads each output, verifies object identities and serialized bytes, and checks streamed resource contents. Unknown streamed-resource owners fail closed. Unmodified audio/resource files are also checked. `finalize_story_traders.py` now applies the same compact profile before native script remapping, so subsequent builds retain the reduction.

## Visit layout

Visit occupies a third slot in the existing Buy/Sell row. Three connected controls built from the native Trading / Tasks / Services tab artwork (sliced outlines, tiled fills, 25 px overlap, cart/dialogue icons and bold selected labels) cover the original tab footprint; the native tab geometry is preserved and its visuals are hidden while the replacement row is active. Native Buy/Sell visuals are restored when Visit is unavailable or disabled. The original native tab handlers still control trading mode and availability. Opening Visit gives the animated room its own screen, with matching Buy/Sell/Visit navigation and a selected Visit state.

The conversation panel adapts to the available width and stays within the lower half of the screen. Dialogue/history and replies use separate scroll areas, so long passages cannot push every reply out of view. Reply height is measured from wrapped text, including explicit line breaks. Leave and Skip remain in the panel footer. Escape, confirmations, history and native item handover retain their existing behavior.

Visit navigation, Leave, Skip and history controls use the recovered Tarkov artwork with consistent hover/pressed/disabled colors. Reply rows follow the recovered TraderDialogWindowOptionRow: warm ivory italic text, transparent idle hit areas, the native dialogue marker, and a muted olive-gray selection highlight with black text and marker. Disabled replies dim both text and marker. Wrapped replies are measured with the same italic font and padding used for display.

Profile switching and trader-room loading now use `CampaignLoadingScreen`, which clones the native PvE loading artwork and preserves the existing animated logo and caption layout. Trader visits show it before story requests, checksum verification and room loading. Checksums run off the main thread, while bundles and prefabs load through Unity's asynchronous APIs. The overlay stays above the visit until the room has rendered. Closing or leaving the trader cancels ownership immediately; an outstanding Unity load finishes and releases its bundle without reopening the cancelled visit. No shared game-loader state is changed.

## Rebuild and validation

For an existing finalized set, run `.tools/Scripts/python.exe tools/compact_story_traders.py`. It writes candidates and a per-texture audit under `artifacts/compact-traders`. `tools/preview_compact_traders.py` creates SDK preview bundles from those exact candidates, changing only reviewed script identities and checking untouched audio resources against the originals.

Sync the UI preview source and current Story/Visit/Peacekeeper builders into the companion SDK, preserving their existing `.meta` files. Run `WTT.Campaigns.Tools.CampaignsStoryPreview.RenderCompact` in the Unity editor in batch mode. It renders every compact room and checks the Visit layout at 1280×720, 1024×768, 1920×1080, 2560×1440 and 3440×1440. The UI checks include long dialogue, many replies, confirmation, history, navigation, busy/skip states, bounds and native tab geometry restoration. Review the rendered images before promotion.

Back up the previous `Client/Resources/StoryMedia/traders` files and manifest, then promote the verified candidate files. Build, run offline contracts and install with `dotnet msbuild build.proj` (or `-p:DeploymentScope=Client` when the matching server/shared installation already exists). MSBuild verifies the room hashes and sizes, backs up replaced installed files and checks installed hashes. No server or game client is launched by this workflow. User-controlled in-game acceptance is still needed for native trader switching and item handover.
