# Trader and task progression

Build 0.4.0 backports captured live loyalty requirements and task progression to **all normal and seasonal characters** on SPT 4.1.3 / EFT 0.16.9.40743. Install the client and server together. The source is the latest supplied PvE capture, not an online database.

## Behavior

- Twelve existing traders receive captured level/reputation thresholds and zero spending requirements. Prices, service coefficients, assortments and trader discovery rules stay intact. Historical spending remains visible.
- The overlay updates 381 existing quests, with 238 in loyalty groups. It adds no quest definitions. Beta-only and custom tasks retain their progression.
- Reputation rewards and penalties use the captured values for each task stage and recipient. Objectives, objective IDs, items, experience and other rewards remain beta content.
- Tiered tasks require their captured loyalty level and any supported live prerequisites. Unsupported global-variable gates become the loyalty gate. Essential tasks with unsupported conditions or missing prerequisites retain their complete beta start conditions.
- Five shared tasks moved between traders in the capture. Their assignments follow live so they appear in the correct groups. The audit records both trader IDs.
- Locked previews respect faction, edition, event, secret-task, trader availability and season restrictions. The server validates acceptance; previews do not start tasks or grant rewards.

Existing reputation, spending and task progress are preserved. Loyalty is recalculated using the new requirements and can decrease. Accepted tasks remain active after a loyalty loss; unaccepted tasks become locked. There is no retroactive reputation payout or save migration receipt.

## Task UI

The native task list gains collapsible Loyalty Level I–IV, Essential Tasks and repeatable-task sections. Existing rows, status colors, quest actions and the detail panel are reused. Empty sections are hidden; collapse choices are saved per trader. The selected task shows its loyalty tier. Show completed and Show locked retain their existing functions.

The native trader header already suppresses a zero spending requirement; the tooltip now hides its spending requirement and checkmark too. Current spending remains informational. Availability refreshes while the task list is open when player level, trader reputation or loyalty changes.

`/wtt-seasonal/progression` is a read-only metadata route with `Version: 1`, a `Quests` dictionary of applied quest IDs to `{TraderId, Tier}`, and the applied `Traders` list. The client caches this per backend session and clears it during reconnect/character switching. Quest actions continue through native SPT routes.

## Data and reproducibility

Run `tools/import_trader_progression.py` with Python 3.12; optional `--dump` and `--database` arguments override the local capture/database roots. It writes `data/trader-progression.json` and `data/trader-progression-audit.json`, including capture filenames and SHA-256 hashes. Only progression data is exported; account state, credentials and complete live quest definitions are excluded.

Run `tools/validate_trader_progression.py` to reproduce the overlay, check unchanged task/objective/non-reputation data, and write `data/trader-progression-reachability.json`. The reachability estimate assumes level 79, zero starting reputation, unlocked traders, completable objectives, terminal quest branches and no optional reputation penalties. It excludes repeatables and other reputation sources and is not a playthrough guarantee.

Under those optimistic assumptions the main traders reach maximum loyalty. Fence is 4.66 reputation short and Ref is 0.59 short from one-time tasks alone. Their captured thresholds remain unchanged; the backport does not add missing quests or manufacture replacement reputation.

## Validation

Contract tests cover every trader threshold, exact/below boundaries, fractional comparisons, maximum loyalty including Fence, imported condition types, and native client hook shapes. The importer audit checks all 381 tasks against the supplied captures.

The server fixture described by earlier validation is retired from the workflow. Use the offline checks and mandatory installation in [build and deployment](build-deployment.md); never stop or start any server or client.

The integration report is written locally to `Research/progression-results.json`. The historical isolated runtime included WTT-ContentBackport; wire comparisons permit that mod's appended weapon, equipment and dogtag IDs while requiring all original objective entries to remain intact. The five integration verification stages passed 3,224 checks, including 3,141 wire-data and maximum-loyalty checks.

In-game visual acceptance at 1920×1080 and the reference aspect ratio remains required. Assembly checks and server tests do not verify actual prefab placement, font rendering, collapsing/scrolling, or live client events. No installed game or real user profile is modified by these tests. Packaging installs the validated client/server update. It never stops or starts applications.
