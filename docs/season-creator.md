# Season Creator (0.3.0)

The creator runs inside the installed SPT 4.1.3 Blazor host at `/wtt-seasonal/creator`. It uses the host’s interactive server rendering, MudBlazor layout, and `Administrator` policy. Install the full client/server package together; authored seasons require protocol 2. The isolated test installation uses https://127.0.0.1:6975/wtt-seasonal/creator.

## Workspace and help

The creator uses a compact charcoal theme with muted gold primary actions, based on the supplied SPT Map Loot Editor video. The section list stays at the left, the editing area occupies the center, and the right inspector holds selected reward settings and the season summary. On smaller screens, the inspector moves below the editing area instead of disappearing.

Hover or focus the **?** markers for explanations of point costs, collection windows, page gates, weighted crate contents and other settings. Tap a marker on touch devices; Escape dismisses focused help. Reward tiles show selection and disabled states. Select a tile to edit its contents and dimensions in the inspector, then use **Save changes** in the top bar.

## Create a season

1. Open the creator through SPT’s web interface with an administrator account. Select **Create blank season** or **Duplicate active season**. Blank seasons use the bundled document model, neutral branding, valid artwork, one empty battle-pass page, and no selected perks.
2. Fill in Overview and Starting character. Choose an installed starter edition or leave it blank to use each account’s normal starter edition. Stash additions include money through the item picker; equipment replaces its selected slot. USEC and BEAR have separate starting items and skill levels. The starting setup is committed once before perk grants.
3. Create personal/common perks from supported effect templates, set parameters and descriptions, and configure points and conflicts. Unsupported imported effects stay visible and unavailable.
4. Define 1–8 document types. Choose a compatible installed item or clone it into season-owned content. Configure dimensions and stack limits under Items and crates. The raid cap is still eight; defaults are eight per raid, thirty per 23-hour window, and a five percent Classified chance.
5. Arrange battle-pass tiles on the 2×3 grid, and seasonal rewards on the 5×2 grid. Drag tiles to move them; edit width and height to resize. Each tile can contain several item, customization, trader-unlock, or local Tarcoin payloads. Set document costs, faction, level and completed-quest requirements. Previous-page requirements count enabled tiles.
6. Create native loot-container items and configure weighted pools, number of rolls, and found-in-raid behavior. Select the exchange crate, or leave it blank to disable that exchange.
7. Create quest chains with Level, Quest, TraderLoyalty, FindItem and HandoverItem conditions. Item objectives use ordinary inventory items. Installed quests can also be chosen directly in reward requirements. Quest objectives, messages, item rewards, XP, skills and trader rewards use SPT’s native contracts.
8. Upload PNG artwork (8 MB maximum, at most 4096×4096), or reuse available images. Edit English text or add translations. Empty/missing translations fall back to the authoritative English fields. Language codes must match the installed game’s locales for the translation to appear in-game.
9. **Save draft**, then **Validate**. Issues link to the relevant section or reward. Simulate level, faction, completed quests, claimed tiles, document balances and perk combinations; preview never reads or changes a player profile.
10. **Publish pack** creates an immutable revision. Download its ZIP to share it. Select **Activate after restart** and restart SPT. The currently running season stays unchanged until restart.

Missing installed dependencies allow publication/export with a dependency report, but block activation on that server. Missing artwork, unsupported enabled behavior and malformed structure block publication. Owned model assets are referenced from installed content; season packs contain no executable code or Unity bundles.

## Storage and recovery

All private authoring files live beside the server mod under `creator/`, outside `wwwroot`:

- `legacy.json`: the first imported bundled season, including existing configuration.
- `drafts/`: explicit saves with revision conflict checks and atomic replacement/backups.
- `packs/`: immutable published revisions, definitions, owned PNGs and checksum manifests.
- `assets/`: uploaded artwork, addressed by content hash.
- `used/`: gameplay hashes of seasons with characters.
- `selection.json`: active/pending pack and the most recent activation error.

Preserve these files and SPT’s profiles/profile data together. Do not remove archived packs that a character uses. Pack downloads contain only manifest-listed definition/artwork files; they never include profiles, credentials or unrelated mod files. ZIP import checks paths, duplicate entries, file sizes, identities and checksums. PNG uploads check bounds and chunk integrity.

A failed pending activation leaves the last valid season selected and shows an error in the library. Draft corruption falls back to its atomic backup when available. Competing editor tabs must reload after a save conflict. Used seasons permit presentation revisions; gameplay edits require **Duplicate as new season**. Duplication remaps owned content and internal references while preserving installed dependencies.

Existing account links migrate lazily to a season-to-character mapping without replacing either profile. The original character belongs to the bundled legacy season. Switching seasons resets the selected mode to Normal; activating an earlier season restores its character and progress. Inactive seasonal sessions cannot claim rewards or perform inventory mutations. Item templates needed by archived characters remain registered; quests and gameplay use only the active definition.

## Implementation map

- `Shared/Seasons`: authoring contracts, canonical compiler, dependency inventory, translation fallback and structural validation.
- `Server/Seasons`: atomic repository, immutable snapshots, early item registration, restart activation and starter setup transactions.
- `Server/Web`: administrator page/components, installed-content pickers, PNG upload, reward layout and simulation, authorized download/image endpoints.
- `Server/Profiles`: legacy link migration, per-season profile resolution and gameplay-hash tracking.
- `Client` and `UI`: protocol checks, document template mappings, season branding, localized text and season/revision asset identity.

## Verification

The implementation was exercised on synthetic profiles in `Testing/Server` only. The installed game/server profiles were not modified.

- 552 contract, storage and installed client-assembly assertions.
- 74 existing native profile/perk integration checks and 173 existing hub checks.
- 28 custom-season checks: starting items/skills, two-quest chain, gated rewards, native crate opening, document exchange, idempotent claims and protocol/season rejection.
- 179 custom-document raid checks: placement, pickup, split/merge, extraction, retries and collection limits.
- Season switching checks cover returning to the legacy character and restoring the custom character’s inventory and claims.
- HTTP checks confirm the creator page, local stylesheet and pack download are served by the SPT host. With authentication enabled, the editor, artwork and download endpoints require login.
- A corrupted pending pack fails checksum preflight and preserves the active pack and character state.

Run the standard contract checks through `tools/package.ps1`. For custom-season integration, first run `tools/test_integration.py` on the isolated legacy season, then `dotnet run --project Tests -- --creator-fixture Testing/Server/user/mods/SeasonalPerks`. Restart the isolated server and run `tools/test_creator.py verify`, followed by `tools/test_hub_raids.py --creator`. These fixtures call the same repository/native gameplay services used by authoring; they do not substitute for browser acceptance.

**Outstanding acceptance:** interactive browser saving, drag/drop, upload and simulation checks, and installed-game creation/branding/layout checks at supported resolutions. The local HTTPS certificate blocked the automated browser session. These gates must pass before treating 0.3.0 as a completed release.
