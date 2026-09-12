# WTT-Campaigns 0.6.1

Beta maintenance release covering changes since [0.6.0](https://github.com/CJ-SPT/SeasonalPerks/releases/tag/V0.6.0).

## Fixes

- **Backport compatible quests and repair reputation progression.** Add Peacekeeper's Demonstration Model after auditing 293 missing quest IDs across the PvE and seasonal captures. Unsupported objectives, unverified zones, incomplete rewards and their dependent quests remain excluded. Adjust completion reputation to remove regular-trader bottlenecks, including +0.50 Ragman reputation for Supplier.
- **Migrate older characters safely.** Completed affected quests receive their positive reputation difference once per character. The server verifies a full profile backup before crediting reputation and saves a receipt with the adjustment. Existing quest progress and non-reputation rewards are retained.
- **Reuse SPT's notification WebSocket for the raid editor.** Presence, draft synchronization and capture updates share the client's existing connection. Editor replies are matched to requests and separated from native notifications; SPT controls reconnection. Install matching client and server components together; the former HTTP authoring routes and separate editor socket are removed.
- **Restore native quest unlock requirements.** Tiered and Essential Tasks retain SPT's prerequisite quests, player-level requirements and unlock delays. Trader loyalty is an additional gate for tiered tasks. Fresh characters can no longer bypass native task chains; for example, The Punisher - Part 3 requires level 19 and completion of Part 2. Accepted and completed tasks retain their progress. The server also repairs older progression data at runtime.
- **Restore access to the main character after a launcher wipe.** The character selector keeps the main character available and correctly recognizes that native character creation is required. Campaign loading overlays release the screen and input while character creation runs.
- **Prevent task tier badges from overlapping long quest titles.** Quest titles reserve space for the badge and truncate with an ellipsis when needed.
- **Limit story enum conversion to story data.** The story JSON converter no longer applies its string-only rules to unrelated game or mod enums.
- **Handle missing bot difficulty settings during raid setup.** Bot roles without server-provided difficulty settings use SPT's assault fallback when available, avoiding the associated raid setup crash.
- **Recover from failed or cancelled raid loading.** Clear the matching character's pending raid and story-session state so another attempt is possible. Cleanup checks the character and raid identity to avoid clearing a different raid.

## Documentation and validation

- Move player and creator guides into the repository's `wiki` directory, with a documentation home, navigation and updated guides. Contributor documentation now lives in `contributing`.
- Expand offline regression coverage for quest prerequisites, character reconnects, story enum compatibility, task badge layout and raid startup/recovery hooks.

## Updating

Targets **SPT 4.1.x / EFT 0.16.9.40743**. Requires **UnityToolkit 2.0.2 or later**, including its prepatcher, and **WTT-ContentBackport 2.0.1 or later** with its dependencies. Dependencies are separate downloads.

With the game and server closed, extract the archive's `BepInEx` and `SPT_Runtime` folders into your SPT installation. Install the full matching package. Preserve existing configuration, profiles and the server mod's `creator` folder. Back up profiles before updating this beta, then manually start the server and game when ready.

## Known limitations

- Ref and Fence retain their existing progression; the reputation rebalance covers the seven regular quest traders.
- Fika is not supported.
- The built-in campaign does not include Kord Breach quests or a complete authored story campaign.
- Some artwork and presentation remain placeholders.

See the [README](README.md), [documentation home](wiki/Home.md) and [compatibility guide](wiki/compatibility.md) for setup and feature limits.
