# Seasonal hub UI 0.1.24

The Seasonal character's main menu gains a KORD BREACH banner opening Battle Pass, Seasonal Rewards and About the Season. Normal characters have no banner. Back or Escape returns to the menu; Q/E and the page arrows browse rewards or the season carousel. Reward selection survives tab changes until the hub closes.

This release is a browsing preview. Claims, purchases/exchanges, leagues and unrecorded tutorial/transaction dialogs are unavailable. Installed data has zero claims and documents, and no countdown. It does not award items, enable online services, import live account progress or change profiles. Captured carousel text describes live season features; synchronization and automatic resets are not implemented.

## Reference evidence

The supplied `Escape From Tarkov 2026.09.05 - 23.19.55.01.mp4` is 25.7327 seconds at 2560x1440. Comparisons exclude window chrome, taskbar and the recording notification.

| Time | Observation |
| --- | --- |
| 0–2 s | KORD BREACH widget above the menu opens Battle Pass. |
| 3–6 s | Tarcoins selected on page 1; central image, requirements and documents. |
| 7–10 s | Paging, mixed tile spans, Scorpion target and Black Herringbone. |
| 11–13 s | Later pages, including the Anton voice reward. |
| 14–18 s | Five seasonal tiles, glasses preview, task requirement and disabled claim. |
| 19–25 s | Five-page carousel, common/positive/negative modifier icons and tooltips. |

The live hub is `level48` GameObject 8658, Battle Pass panel 4032; the recovered widget is `level50` 752. Carousel data is `sharedassets48.assets` object 2693, referencing sprites 389/480/324/500/493. Logo and smoke are `StreamingAssets/Video/SeasonLogo/Season_1_logo_video_1380x460.webm` and `Smoke_1144x264.webm`. Widget sound types 75/74 resolve through `UISoundsWrapper`; its hover loop is `sharedassets44.assets` AudioClip 452 with interface-mixer output, 0.3 volume and 0.1/0.3-second fades.

The supplied PDB GUID and age match the live GameAssembly CodeView entry. Native disassembly confirms the full reward view uses the reward's `Image` field at offset 0x58. Disassembly and the match report remain in ignored `Research/native`.

The HTTP dump supplies 12 pages/53 rewards, eight document schemes and five seasonal rewards. Universal-document images come separately from `client.globals` → `BattlePassUniversalDocument`. The importer reads only catalogue, templates and localization; it does not read profile responses or request headers.

## Reproduction

1. Run `tools/recover_hub.py` and `tools/recover_profile_audio.py` using the existing Python environment. Source/object IDs and hashes are recorded in CJ-SDK's SeasonalPerks asset workspace.
2. Run `tools/import_hub.py --download-images` with the supplied captures; omit the download flag when all images are local. It writes sanitized presentation data and a 102-image allowlist. Downloads occur only during development.
3. Run `tools/sync_ui_preview.py`. In Unity 2022.3.43f1 use **SDK / Seasonal Perks / Build recovered UI**, **Render season hub previews**, and the existing **Render UI previews**.
4. Run Release builds, contract/native UI checks and `tools/test_hub.py` against the isolated server. Stage with the existing packaging scripts.

Decorative sprites, fonts, audio and videos are bundled. Catalogue images use `/seasonal-perks/hub-images/{id}.png`; perk icons retain their original route and stay outside the bundle. `/seasonal-perks/hub` is a separate read-only route without profile mutation. Both client and server must be updated together.

Shared contracts and UI models are separate, keeping presentation free of EFT/SPT dependencies. Client adapters own menu availability, bounded image requests, audio and video lifecycle. Closing cancels queued work, discards stale responses, releases image textures and stops video decoders. Catalogue failures offer Retry; individual images show an unavailable label and retry on a fresh opening.

Previews decode image bytes just as runtime does. Unity's default non-power-of-two texture import would otherwise distort aspect ratios. Synthetic owned/claimed states are preview fixtures and are never served by the installed mod.

## Validation

Unity checks cover every reward/page, tab persistence, boundaries, carousel, tooltips, unavailable actions, fresh opening and retry at 1920x1080, 2560x1440 and 1902x992. Gamma is used temporarily and SDK settings are restored. Server checks verify every image hash and unchanged isolated profiles. In-game placement, animation, audio and reconnect behavior are separate runtime checks; preview renders alone do not establish exact visual parity.

The 0.1.24 validation run passed 382 contract/client assertions, 31 native UI compatibility checks, 173 isolated-server checks and 133 hub interaction checks at each of the three resolutions. The existing UI regression passed 96 interaction checks plus 14 modifier checks at each resolution. Release compilation and formatting checks passed. Bundle inspection found only standard uGUI MonoScripts, the three recovered hub sounds and both original video clips.

Installed SPT 4.1.3 checks confirmed banner entry, Q/E paging, reward selection, tab restoration, Seasonal Rewards, the About carousel, modifier tooltips, disabled-action explanations, Escape-to-menu and hiding the banner after switching to Normal. Runtime inspection caught and corrected the native beta-notice overlap and video frame binding. The static logo fallback now yields to the decoded video. The banner sits above SPT's beta notice, which is retained.

Exact visual/audio parity is not signed off by these checks. Audible mixing, raid-entry cleanup, reconnects and deliberate network-failure/close-during-load timing still require runtime acceptance. Catalogue/image requests observed in the game use the local server; no external artwork access is used by the implementation. External networking was not disabled globally during the installed-game check.
