# Seasonal hub UI 0.2.0

Current workflow: [build, validate and always install](build-deployment.md). Never stop or start servers or clients. Any isolated-server results below are historical; those fixtures are retired.

The Seasonal character's main menu gains a KORD BREACH banner opening Battle Pass, Seasonal Rewards and About the Season. Normal characters have no banner. Back or Escape returns to the menu; Q/E and the page arrows browse rewards or the season carousel. Reward selection survives tab changes until the hub closes.

The hub now includes local claim, Classified-shortage confirmation, exchange and result dialogs, plus the Battle Pass tutorial. Fresh Seasonal progress starts at zero, with no season countdown. Leagues, ratings and online purchases remain unavailable. See [gameplay, dependency locks and asset reproduction](battle-pass-gameplay.md) for transaction behavior and validation. Captured carousel text describes live season features; online synchronization and automatic resets are not implemented.

## Battle Pass tutorial

The information button beside the season badge replays the eight-step tutorial. On the first successful opening it also starts automatically, unless a transaction needs reconciliation. NEXT, E, Right Arrow, Space or Enter advances; PREVIOUS, Q or Left Arrow goes back. Clicking the highlighted region advances without activating the control beneath it. Escape closes only the tutorial. FINISH or SKIP TUTORIAL saves a local seen preference; Escape leaves it eligible for the next opening. Replay remains available after completion. The preference is installation-wide (`WTTSeasonal/UITutorial/BattlePass/v1`), independent of EFT's native tutorial preference and player progression.

The sequence comes from live `level48`'s recovered `BattlePassUITutorial` component 39485: rewards, requirements, ordinary documents, document information, Classified documents, collection limit, exchange, then the final system briefing. Text references are retained in `data/locales/hub-en.json` under `UITutorial/BattlePass*`. The info icon (`sharedassets48-454`) and briefing background (`sharedassets48-400`) are already in the artwork bundle.

The local version retains this structure and mint focus treatment while describing SPT behavior: physical raid documents, the server-provided allowance/window, local character progress and dependency-gated crates. It does not repeat live's private quest-item visibility, cross-mode synchronization or reward-retention promises. Document hover information and the forced step-four information card describe the local loot containers. The final briefing supports a completion key or FINISH button; Q/Left Arrow still goes back and Escape still cancels.

The overlay blocks underlying mouse, scroll, selection and transaction controls. It preserves the selected reward/page and is removed on loading, state refresh, result dialogs, hub closure or disposal. Completion changes only the local preference. Pending transactions and error/loading screens cannot be replaced by automatic onboarding.

The tutorial update passes 247 Unity hub interaction/layout checks at each of 1920×1080, 2560×1440 and 1902×992, including all eight steps, text fitting, replay, navigation restoration, transaction isolation and cleanup. All 24 step previews are in ignored `Research/UI/hub-tutorial-*`; document information and final-briefing renders were visually reviewed. The full Release solution build and 42 native UI compatibility checks pass. These are editor/build checks; installed in-game acceptance remains pending.

The exchange window follows the supplied September 6 live screenshot: a wide dark window, title-bar close button, four-column document grid, circular arrows, and illustrated DOCUMENTS / CONTAINER choices. Choose a category, then click an owned document to add one; click its selected stack above to remove one. Document exchanges also require an explicit target document. Container availability and both costs still come from the server. The information icon explains selection and costs.

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

The separate exchange window is `level49` GameObject 3965. `tools/recover_hub.py` also recovers this hierarchy and its artwork, including `sharedassets49` sprites 370/315 (category illustrations), 295/327 (inactive/active arrows), and the document shadows and close button. Its native window is 1150 × 565; the supplied screenshot uses approximately 125% scale. Rebuild the UI bundle after recovery so the client receives these additional sprites.

The supplied PDB GUID and age match the live GameAssembly CodeView entry. Native disassembly confirms the full reward view uses the reward's `Image` field at offset 0x58. Disassembly and the match report remain in ignored `Research/native`.

The HTTP dump supplies 12 pages/53 rewards, eight document schemes and five seasonal rewards. Universal-document images come separately from `client.globals` → `BattlePassUniversalDocument`. The importer reads only catalogue, templates and localization; it does not read profile responses or request headers.

## Reproduction

1. Run `tools/recover_hub.py` and `tools/recover_profile_audio.py` using the existing Python environment. Source/object IDs and hashes are recorded in CJ-SDK's SeasonalPerks asset workspace.
2. Run `tools/import_hub.py --download-images` with the supplied captures; omit the download flag when all images are local. It writes sanitized presentation data and a 102-image allowlist. Downloads occur only during development.
3. Run `tools/sync_ui_preview.py`. In Unity 2022.3.43f1 use **SDK / Seasonal Perks / Build recovered UI**, **Render season hub previews**, and the existing **Render UI previews**.
4. Run Release builds, contract/native UI checks and `tools/test_hub.py` against the isolated server. Stage with the existing packaging scripts.

Decorative sprites, fonts, audio and videos are bundled. Catalogue images use `/wtt-seasonal/hub-images/{id}.png`; perk icons retain their original route and stay outside the bundle. `/wtt-seasonal/hub` is a separate read-only route without profile mutation. Both client and server must be updated together.

Shared contracts and UI models are separate, keeping presentation free of EFT/SPT dependencies. Client adapters own menu availability, bounded image requests, audio and video lifecycle. Closing cancels queued work, discards stale responses, releases image textures and stops video decoders. Catalogue failures offer Retry; individual images show an unavailable label and retry on a fresh opening.

Previews decode image bytes just as runtime does. Unity's default non-power-of-two texture import would otherwise distort aspect ratios. Synthetic owned/claimed states are preview fixtures and are never served by the installed mod.

## Validation

Unity checks cover every reward/page, tab persistence, boundaries, carousel, tooltips, unavailable actions, fresh opening and retry at 1920x1080, 2560x1440 and 1902x992. Gamma is used temporarily and SDK settings are restored. Server checks verify every image hash and unchanged isolated profiles. In-game placement, animation, audio and reconnect behavior are separate runtime checks; preview renders alone do not establish exact visual parity.

The 0.1.24 validation run passed 382 contract/client assertions, 31 native UI compatibility checks, 173 isolated-server checks and 133 hub interaction checks at each of the three resolutions. The existing UI regression passed 96 interaction checks plus 14 modifier checks at each resolution. Release compilation and formatting checks passed. Bundle inspection found only standard uGUI MonoScripts, the three recovered hub sounds and both original video clips.

Installed SPT 4.1.3 checks confirmed banner entry, Q/E paging, reward selection, tab restoration, Seasonal Rewards, the About carousel, modifier tooltips, disabled-action explanations, Escape-to-menu and hiding the banner after switching to Normal. Runtime inspection caught and corrected the native beta-notice overlap and video frame binding. The static logo fallback now yields to the decoded video. The banner sits above SPT's beta notice, which is retained.

Exact visual/audio parity is not signed off by these checks. Audible mixing, raid-entry cleanup, reconnects and deliberate network-failure/close-during-load timing still require runtime acceptance. Catalogue/image requests observed in the game use the local server; no external artwork access is used by the implementation. External networking was not disabled globally during the installed-game check.

The 0.2.0 editor run adds claim/Classified confirmation, exchange, result and dialog-cleanup fixtures: 141 checks at each requested resolution. Native compatibility also covers document stack events and backend raid boundaries. Gameplay/restart/raid results and remaining dependency locks are documented in [Battle Pass gameplay](battle-pass-gameplay.md). Installed checks above describe the earlier browsing release; they do not sign off the new transactions.

The September 6 exchange-layout update passes 149 editor checks at each of 1920×1080, 2560×1440 and 1902×992, including mixed-source selection/removal, explicit target selection, container availability, switching back from a higher container cost, and title-bar close. Empty, selected-document and locked-container previews are rendered in `Research/UI`; the client Release build has no warnings or errors and the recovered artwork bundle is rebuilt. At 16:49 local time, the UI assembly and artwork bundle were installed into SPT with matching package checksums; previous files are retained under `Testing/UIBackups/Exchange-20260906-164936-092`. The installed client passed 42 native UI compatibility checks. In-game verification of the exchange screen remains pending.
