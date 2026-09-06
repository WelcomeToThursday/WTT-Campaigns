# Battle Pass gameplay 0.2.0

The Seasonal hub now supports local reward claims, document exchanges, raid document acquisition and saved progress. WTT-Seasonal owns the eight ordinary document templates and both season crate templates. Existing content supplied by WTT-ContentBackport is resolved after mod loading; missing or unsupported dependencies keep individual rewards locked. No WTT-CommonLib quest importer or NuGet dependency is introduced.

## Captured catalogue

The amended Seasonal capture supplies 12 pages, 53 Battle Pass tiles and 58 payloads, eight documents and five seasonal rewards. Forty tile costs changed; their total remains 501 ordinary documents. Ordering, tile spans, faction labels, previous-page counts, localization, level comparisons and quest targets are preserved. PvE contributes prerequisite quest definitions and localization. The importer reads public definitions, not request headers or account progress.

The quest dependency closure contains 41 definitions and one missing definition. Historical Perspectives remains unavailable. The compatibility adapter accepts only verified native condition families and existing SPT definitions; unsupported story conditions, encounter dependencies, quest-item placements and unresolved dependency graphs block their chains. Dialogue references never create a quest. Native seasonal quest completion is checked on the active PMC; synthetic completion appears only in isolated test fixtures.

Trader rewards map captured offer IDs to installed offers using trader, root item, attachment/slot structure and loyalty level. Ambiguous matches remain unavailable. Verified non-currency barters can supply a missing native offer. Existing monetary offers use SPT's current prices and the existing Seasonal price adapter. Native stock, loyalty, purchase limits and quest restrictions remain in force. Locked offers are filtered from Seasonal trader/flea results and rejected when purchased directly. Fallback offers are hidden from Normal profiles.

## Documents and transactions

The default is up to eight ordinary documents per Seasonal PMC raid, selected with equal type weights. Each occupies a different eligible jacket, filing drawer, safe or duffel. Container filters and free grid cells are checked; existing loot is retained. Normal and Scav raids receive no injection. Captured map-specific caps are retained as reference data, not treated as probabilities.

Optional `hub-config.json` beside the server DLL accepts `DocumentsPerRaid` (0–8), `MapCounts` (map names to 0–8) and `ClassifiedChancePercent` (0–100, default 5). Missing configuration uses defaults. The catalogue and all installed image/model dependencies operate locally.

The first pickup starts a server-timed 23-hour window with a 30-document allowance. Each spawned unit has a persistent identity, including units subsequently merged into another stack or split into a new stack. Brought-in units are tracked separately. Repeat pickups and operation retries do not consume the allowance again. The client journals pickup/stack operations before transmission and flushes them before native raid start/end. Unfinished server raid receipts permit reconciling those known operations after reconnecting. Spawn counts also respect remaining allowance.

Survived and run-through extractions roll once per newly acquired extracted unit for a Classified document. The default is 5%; ordinary documents remain in inventory. Failed raids award no bonus. Raid receipts prevent repeated extraction requests from rerolling bonuses or replaying the native inventory update. Starting another raid closes abandoned receipts.

Claims consume the required ordinary types first. Classified documents cover the exact shortage 1:1 only after confirmation. Five mixed ordinary documents exchange for one selected ordinary type; ten exchange for the captured gear crate when its contents dependency is available. Classified documents are excluded from exchange sources. Online purchases remain disabled.

Every payload is preflighted and applied to a cloned profile. Physical items use SPT's stash placement helper, without sorting-table or mail fallback. Missing dependencies, shortages, full stash and stale revisions reject the entire transaction. Customizations, trader unlocks and local Tarcoins use separate adapters. A multi-payload tile adds one claim.

## Persistence and lifecycle

`/seasonal-perks/hub` remains read-only. `/seasonal-perks/hub/claim`, `/exchange` and `/raid-document` resolve operations on the server. Claim/exchange requests carry an operation ID and expected revision. Repeating a committed operation returns its receipt and current state; changing its inputs is rejected.

State resides in the Seasonal PMC's extension data under `wttSeasonalHub:{season}:{battlePass}`. It includes claims, Classified/Tarcoin balances, allowance windows, conserved raid units, trader unlocks and transaction receipts. Ordinary balances come from inventory. New state starts at zero; the season has no expiry or automatic wipe.

Transactions and native inventory operations reuse the account lock. The commit adapter atomically replaces the verified SPT 4.1 profile-cache entry, then uses SPT's atomic profile save. It restores the original cache on write failure. SPT's `GetProfiles()` returns a copy and must not be used for replacement.

The client flushes native inventory operations before transactions. Pending claim/exchange operation IDs are saved locally before submission and reconciled after reopening or restarting. Successful transactions use the existing controlled profile reload and preserve the hub tab, page and selected tile. Closing cancels presentation loads; it does not cancel a committed transaction. Raid entry, character changes and teardown close the hub and release presentation resources.

## Reproduce the item assets

1. Import amended presentation with `tools/import_hub.py --mode-dump "<Development>/1.0 Dump"`, then gameplay with `tools/import_hub_gameplay.py --dump "<Development>/1.0 Dump"`.
2. Run `tools/import_season_items.py --dump "<Development>/1.0 Dump"`. It preserves original template IDs, source hashes, object IDs and preview settings, and exports original compressed texture bytes with every mip level into the ignored SDK workspace.
3. Copy `tools/unity/SeasonalItemBuilder.cs` to the companion SDK's SeasonalPerks `Editor` folder and execute `SeasonalItemBuilder.Build` in Unity 2022.3.43f1. It rebuilds nine shared item prefabs using SDK components, meshes, materials and textures. Bundle keys use `wtt-seasonal/` to avoid collisions with the content backport.
4. Run `tools/finalize_season_items.py`. It verifies compressed texture bytes, dimensions, mip counts and formats; restores original texture sampling metadata; and maps the SDK-generated PreviewPivot reference to the installed SPT native type. No live MonoScript implementation or generated preview component is bundled. The native reference is audited against `globalgamemanagers.assets` object 2768.
5. Run `tools/sync_ui_preview.py`; copy the reviewed `tools/unity/SeasonalHubPreview.cs` into the SDK editor folder and render its fixtures. Gamma is used and the previous SDK setting is restored.
6. Build and stage through `tools/package.ps1`. Packaging requires all nine audited bundles and all allowlisted catalogue images. Raw captures, media, binaries and generated assets remain local and ignored.

The documents retain the captured 999-unit stack limit and dimensions, including 2×2 blueprints. Their unsupported live BattlePassItem parent is adapted to SPT's native information-item parent. Crates retain their native random-container parent, but the capture contains no verified contents pool. Their claims and exchange stay unavailable until one is supplied; no contents are invented.

## Validation and remaining limits

Automated checks cover every tile's eligibility and all four reward adapters, amended costs, page gates, confirmed Classified shortages, mixed-source exchanges, dependency failures, full stash, duplicate/concurrent requests, forced save failure, restart and idempotency. Separate real raid-route checks cover distinct eligible containers, pickup limits, conserved split identities, survived/run-through extraction, duplicate raid-end handling, and Normal/Scav exclusion. Normal profiles, Scav data and unrelated PMC fields are compared separately from intentional Seasonal changes.

The tested installed content set leaves 22 tiles unavailable: eight crate tiles without a contents pool, nine unsupported/missing customization definitions, and five seasonal rewards gated by unavailable quest chains. Those are explicit dependency locks. The other 36 tiles exercise 41 payloads in isolated fixtures. Actual profile progress is never imported from the recording or captures.

Unity interaction fixtures cover 1920×1080, 2560×1440 and 1902×992, including claims, Classified confirmation, exchanges, result/error dialogs, paging and tab restoration. These are editor renders, not evidence of installed game parity. Animation, audio, item rendering, native inventory event timing, and repeated menu/profile reloads still require an in-game acceptance pass. Do not declare visual parity from automated results alone.

The September 6, 2026 automated run passed:

| Check | Result |
| --- | --- |
| Contract, catalogue and native compatibility assertions | 520 |
| Native UI/event bindings | 42 |
| Isolated claim/exchange checks | 589 |
| Persisted state and operation receipts after restart | 102 |
| Native raid-route checks | 171 |
| Existing account/profile integration checks | 74 |
| Read-only hub and image checks | 173 |
| Hub Unity interactions | 141 per resolution |
| Existing Unity interactions and modifiers | 96 + 14 per resolution |
| Recovered item bundles | Nine audited bundles; original compressed texture data and mip levels verified |
| Release build and formatting | Passed; zero build warnings/errors |

The SDK project-settings hash was unchanged after the Gamma preview runs. Native resource, bush and experience hook checks also passed. The versioned release is staged locally; it has not been installed or accepted in-game.

To repeat server checks, start the isolated runtime and run `tools/test_integration.py` to create a fresh synthetic account, followed by `tools/test_hub.py`. Stop that runtime before `tools/test_hub_gameplay.py prepare`, then restart it for `tools/test_hub_gameplay.py verify`. Restart once more for `tools/test_hub_gameplay.py restart`. Run `tools/test_hub_raids.py` against the fresh integration account; its five-raid sequence intentionally exhausts that account's allowance. These tools use only `Testing/Server` and do not modify installed profiles. The reported dependency outcomes used the installed content backport and its own dependency copied into the isolated runtime.
